using EventStore.Client.Lite;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Kubernetes.Bootstrapper.One.App
{
    [Route("One")]
    public class OneController : Controller
    {
        private IEventStoreRepository<Guid> _repository;
        private IEventStoreCache<Guid, Thing> _eventStoreCache;
        private MyAppConfig _myAppConfig;
        private MyGroupConfig _myGroupConfig;

        public OneController(IEventStoreRepository<Guid> repository, IEventStoreCache<Guid, Thing> eventStoreCache, MyAppConfig myAppConfig, MyGroupConfig myGroupConfig)
        {
            _repository = repository;
            _eventStoreCache = eventStoreCache;
            _myAppConfig = myAppConfig;
            _myGroupConfig = myGroupConfig;
        }

        [HttpGet("config/group")]
        public string GetGroupConfig()
        {
            return JsonConvert.SerializeObject(_myGroupConfig);
        }

        [HttpGet("config/app")]
        public string GetAppConfig()
        {
            return JsonConvert.SerializeObject(_myAppConfig);
        }

        [HttpPost("{itemId}")]
        public async Task SetValue(Guid itemId, [FromForm] string value)
        {
            var item = await _repository.GetById<Thing>(itemId);

            if (null == item)
            {
                item = new Thing()
                {
                    EntityId = itemId
                };
            }

            _repository.Apply(item, new DoThingEvent() { Value = value });

        }

        [HttpGet("{itemId}")]
        public async Task<IActionResult> GetValue(Guid itemId)
        {
            var item = await _repository.GetById<Thing>(itemId);

            if (null == item) return NotFound();

            return Ok(item.Value);  
        }
    }
}
