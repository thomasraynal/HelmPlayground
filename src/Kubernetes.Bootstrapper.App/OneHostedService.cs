using DynamicData;
using EventStore.Client.Lite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kubernetes.Bootstrapper.One.App
{
    public class OneHostedService : IHostedService
    {
        private readonly IEventStoreRepository<Guid> _repository;
        private readonly IEventStoreCache<Guid, Thing> _cache;
        private readonly ILogger<OneHostedService> _logger;
        private IDisposable _disposable;

        public OneHostedService(IEventStoreRepository<Guid> repository, IEventStoreCache<Guid, Thing> cache, ILogger<OneHostedService> logger)
        {
            _repository = repository;
            _cache = cache;
            _logger = logger;

        }

        public Task StartAsync(CancellationToken cancellationToken)
        {

    
            _disposable = _cache
                            .AsObservableCache()
                            .Connect()
                            .FilterEvents(blob => true)
                            .Subscribe(changes =>
                            {
                                foreach(var change in changes)
                                {
                                    _logger.LogInformation(change.Current.Value);
                                }

                            });

 

            Task.Run(async() =>
            {

                while (true)
                {
                    await Task.Delay(10000);

                    var item = new Thing();
                    _repository.Apply(item, new DoThingEvent() { Value = $"{Guid.NewGuid()}" });

                }


            });

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _disposable.Dispose();
        
            return Task.CompletedTask;
        }
    }
}
