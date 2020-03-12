using EventStore.Client.Lite;
using Microsoft.AspNetCore.Mvc;
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
        private IEventStoreCache<Guid, Item> _eventStoreCache;

        public OneController(IEventStoreRepository<Guid> repository, IEventStoreCache<Guid, Item> eventStoreCache)
        {
            _repository = repository;
            _eventStoreCache = eventStoreCache;
        }

        [HttpPost("{itemId}")]
        public async Task SetValue(Guid itemId, [FromForm] string value)
        {
            var item = await _repository.GetById<Item>(itemId);

            if (null == item)
            {
                item = new Item()
                {
                    EntityId = itemId
                };
            }

            _repository.Apply(item, new DoThingEvent() { Value = value });

        }

        [HttpGet("{itemId}")]
        public async Task<IActionResult> GetValue(Guid itemId)
        {
            var item = await _repository.GetById<Item>(itemId);

            if (null == item) return NotFound();

            return Ok(item.Value);  
        }
    }
}
