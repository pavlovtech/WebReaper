using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WebReaper.Core.Scheduler.Abstract;
using WebReaper.Domain;

namespace WebReaper.Core.Scheduler.Concrete;

public class FileScheduler : IScheduler
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
        if (DataCleanupOnStart)
        {
            if(File.Exists(_fileName)) File.Delete(_fileName);
            if(File.Exists(_currentJobPositionFileName)) File.Delete(_currentJobPositionFileName);
        }
        
        var fileInfo = new FileInfo(_fileName);
        fileInfo.Directory?.Create();
        
        var fileInfo2 = new FileInfo(_currentJobPositionFileName);
        fileInfo2.Directory?.Create();

        if (File.Exists(_currentJobPositionFileName))
            _currentJobPosition = int.Parse(await File.ReadAllTextAsync(_currentJobPositionFileName));
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

            var job = JsonConvert.DeserializeObject<Job>(jobLine);
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

    private static string SerializeToJson(Job job)
    {
        return JsonConvert.SerializeObject(job, Formatting.None);
    }
}