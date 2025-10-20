

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Data.Common;

namespace IM.EventStore;



public interface ISubscription
{
    static abstract Task Handle(IEvent @event, IServiceProvider sp, CancellationToken ct);
}

