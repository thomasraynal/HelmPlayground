using EventStore.Client.Lite;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kubernetes.Bootstrapper.One.App
{
    public class Item : AggregateBase<Guid>
    {
        public string Value { get;  set; }
    }
}
