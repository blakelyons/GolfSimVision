using System;
using System.Threading;

namespace GolfSimVision.Tests.TestSupport;

public static class WaitUntil
{
    public static bool True(Func<bool> predicate, TimeSpan? timeout = null, TimeSpan? interval = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        var pollInterval = interval ?? TimeSpan.FromMilliseconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return true;
            Thread.Sleep(pollInterval);
        }
        return predicate();
    }
}
