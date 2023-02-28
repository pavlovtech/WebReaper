using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WebReaper.Domain;
using WebReaper.Scheduler.Abstract;
using static System.IO.File;

namespace WebReaper.Scheduler.Concrete;

public class FileScheduler : IScheduler
{
    private readonly string _fileName;
    private readonly string _currentJobPositionFileName;
    private readonly ILogger _logger;
    private long _currentJobPosition = 0;
    
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public FileScheduler(string fileName, string currentJobPositionFileName, ILogger logger)
    {
        _fileName = fileName;
        _currentJobPositionFileName = currentJobPositionFileName;
        _logger = logger;
        if (File.Exists(_currentJobPositionFileName))
        {
            _currentJobPosition = int.Parse(ReadAllText(_currentJobPositionFileName));
        }
    }
    
    public async IAsyncEnumerable<Job> GetAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"Start {nameof(FileScheduler)}.{nameof(GetAllAsync)}");
        
        using var sr = new StreamReader(_fileName, new FileStreamOptions
        {
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite
        });

        for (int i = 0; i < _currentJobPosition; i++)
        {
            _logger.LogInformation("Skipping {Count} line", i);
            await sr.ReadLineAsync();
        }

        while(true)
        {
            await _semaphore.WaitAsync(cancellationToken);
            string? jobLine;
            try
            {
                jobLine = await sr.ReadLineAsync();
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
            
            await WriteAllTextAsync(_currentJobPositionFileName, $"{_currentJobPosition++}", cancellationToken);
            
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
            await AppendAllTextAsync(_fileName, SerializeToJson(job) + Environment.NewLine, cancellationToken);
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
            await AppendAllLinesAsync(_fileName, serializedJobs, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static string SerializeToJson(Job job) => JsonConvert.SerializeObject(job, Formatting.None);
}