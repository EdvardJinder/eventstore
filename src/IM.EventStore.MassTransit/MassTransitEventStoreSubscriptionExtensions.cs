

//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.DependencyInjection.Extensions;

//namespace IM.EventStore.MassTransit;

//public static class MassTransitEventStoreSubscriptionExtensions
//{
//    public static IEventStoreBuilder AddMassTransitEventStoreSubscription(this IEventStoreBuilder builder)
//    {
//        builder.ConfigureServices(s =>
//        {
//            s.TryAddSingleton<MassTransitEventStoreInterceptor>();
//        });
//        builder.ConfigureDbContextOptionsBuilder((IServiceProvider sp, DbContextOptionsBuilder opts) =>
//        {
//            opts.AddInterceptors(sp.GetRequiredService<MassTransitEventStoreInterceptor>());
//        });
//        return builder;
//    }
//}
