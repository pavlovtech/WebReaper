using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using WebReaper.Domain;

namespace WebReaper.Queue;

public class PubSubPriorityQueue
    : IProducerConsumerCollection<Job>
{
    // Used for enforcing thread-safety
    private object _lockObject = new object();

    public PubSubPriorityQueue() => JobQueue = new(1000);

    public PubSubPriorityQueue(IEnumerable<(Job, int)> collection) =>
        JobQueue = new(collection);

    public PubSubPriorityQueue(params (Job, int)[] collection) =>
        JobQueue = new PriorityQueue<Job, int>(collection);

    protected PriorityQueue<Job, int> JobQueue { get; set; }

    public int Count => JobQueue.Count;

    // Support for ICollection
    public bool IsSynchronized => false;

    public object SyncRoot => _lockObject;

    public void CopyTo(Job[] array, int index)
    {
        lock (_lockObject) JobQueue.UnorderedItems
            .Select((job, priority) => job.Element)
            .ToArray()
            .CopyTo(array, index);
    }

    public void CopyTo(Array array, int index)
    {
        lock (_lockObject) JobQueue.UnorderedItems
            .Select((job, priority) => job.Element)
            .ToArray()
            .CopyTo(array, index);
    }

    public IEnumerator<Job> GetEnumerator()
    {
        lock (_lockObject)
        {
            return JobQueue.UnorderedItems
                .Select((job, priority) => job.Element)
                .ToList()
                .GetEnumerator();
        }
    }

    public Job[] ToArray()
    {
        return JobQueue.UnorderedItems
            .Select((job, priority) => job.Element)
            .ToArray();
    }

    public bool TryAdd(Job item)
    {
        lock (_lockObject) JobQueue.Enqueue(item, item.Priority);

        return true;
    }

    public bool TryTake([MaybeNullWhen(false)] out Job item)
    {
        lock (_lockObject)
        {
            if (JobQueue.TryDequeue(out var job, out var priority))
            {
                item = job;
                return true;
            }

            item = default;
            return false;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}