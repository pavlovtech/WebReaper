using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using WebReaper.Core.Scheduler.Abstract;
using WebReaper.DataAccess;
using WebReaper.Domain;
using WebReaper.Serialization;

namespace WebReaper.Core.Scheduler.Concrete;

internal class FileScheduler : IScheduler
{
    private readonly string _currentJobPositionFileName;
    private readonly string _fileName;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private long _currentJobPosition;
    public Task Initialization { get; }

    public bool DataCleanupOnStart { get; set; }
    
    public FileScheduler(string fileName, string currentJobPositionFileName, ILogger logger, bool dataCleanupOnStart = false)
    {
        DataCleanupOnStart = dataCleanupOnStart;
        _fileName = fileName;
        _currentJobPositionFileName = currentJobPositionFileName;
        _logger = logger;

        Initialization = InitializeAsync();
    }
    
    private async Task InitializeAsync()
    {
        // ADR-0011: directory creation, cleanup-on-start and the missing-file
        // policy are delegated to FilePersistencePrep, for both the job file
        // and the position file. The resumable cursor + poll loop in
        // GetAllAsync is this adapter's own essence and is byte-unchanged (the
        // file-as-queue is the named #58 candidate, out of scope here).
        FilePersistencePrep.CleanupOnStart(_fileName, DataCleanupOnStart);
        FilePersistencePrep.CleanupOnStart(_currentJobPositionFileName, DataCleanupOnStart);

        FilePersistencePrep.EnsureDirectory(_fileName);
        FilePersistencePrep.EnsureDirectory(_currentJobPositionFileName);

        var posText = await FilePersistencePrep.ReadAllTextOrNullAsync(_currentJobPositionFileName);
        if (posText is not null)
            _currentJobPosition = int.Parse(posText);
    }

    public async IAsyncEnumerable<Job> GetAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"Start {nameof(FileScheduler)}.{nameof(GetAllAsync)}");

        using var sr = new StreamReader(_fileName, new FileStreamOptions
        {
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite
        });

        for (var i = 0; i < _currentJobPosition; i++)
        {
            _logger.LogInformation("Skipping {Count} line", i);
            await sr.ReadLineAsync(cancellationToken);
        }

        while (true)
        {
            await _semaphore.WaitAsync(cancellationToken);
            string? jobLine;
            try
            {
                jobLine = await sr.ReadLineAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }

            if (jobLine is null)
            {
                _logger.LogInformation("Job is empty");
                await Task.Delay(300, cancellationToken);
                continue;
            }

            _logger.LogInformation("Writing current job position to file");

            await File.WriteAllTextAsync(_currentJobPositionFileName, $"{_currentJobPosition++}", cancellationToken);

            _logger.LogInformation("Deserializing the job and returning it to consumer");

            var job = WebReaperJson.DeserializeJob(jobLine);
            yield return job;
        }
    }

    public async Task AddAsync(Job job, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"Start {nameof(FileScheduler)}.{nameof(AddAsync)} with one job");

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(_fileName, SerializeToJson(job) + Environment.NewLine, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task AddAsync(IEnumerable<Job> jobs, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"Start {nameof(FileScheduler)}.{nameof(AddAsync)} with multiple jobs");

        var serializedJobs = jobs.Select(SerializeToJson);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllLinesAsync(_fileName, serializedJobs, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // ADR 0008: same WebReaperJson grammar as the config payload and the
    // other schedulers — the ADR-0005 Job round-trip asymmetry is closed
    // uniformly across every IScheduler, not just RedisScheduler.
    private static string SerializeToJson(Job job) => WebReaperJson.SerializeJob(job);
}