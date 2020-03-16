using EventStore.Client.Lite;
using System;
using System.Collections.Generic;
using System.Text;

namespace HelmPlayground.One.App
{
    public class DoThingEvent : EventBase<Guid, Thing>
    {
        public string Value { get; set; }

        protected override void ApplyInternal(Thing entity)
        {
            entity.Value = Value;
        }
    }
}
