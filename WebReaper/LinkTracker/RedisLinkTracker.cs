﻿using StackExchange.Redis;
using WebReaper.LinkTracker.Abstract;

namespace WebReaper.LinkTracker;

public class RedisCrawledLinkTracker : ICrawledLinkTracker
{
    private static ConnectionMultiplexer? redis;

    public RedisCrawledLinkTracker(string connectionString)
    {
        redis = ConnectionMultiplexer.Connect(connectionString);

        try
        {
            // TODO: remove this line
            var srv = redis.GetServer("redis-14134.c135.eu-central-1-1.ec2.cloud.redislabs.com:14134");
            srv.FlushDatabase();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    public async Task AddVisitedLinkAsync(string siteUrl, string visitedLink)
    {
        IDatabase db = redis.GetDatabase();
        await db.SetAddAsync(siteUrl, visitedLink);
    }

    public async Task<IEnumerable<string>> GetVisitedLinksAsync(string siteUrl)
    {
        IDatabase db = redis.GetDatabase();
        var result = await db.SetMembersAsync(siteUrl);

        return result.Select(x => x.ToString());
    }
}