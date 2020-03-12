using EventStore.Client.Lite;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kubernetes.Bootstrapper.One.App
{
    public class DoThingEvent : EventBase<Guid, Item>
    {
        public string Value { get; set; }

        protected override void ApplyInternal(Item entity)
        {
            entity.Value = Value;
        }
    }
}
