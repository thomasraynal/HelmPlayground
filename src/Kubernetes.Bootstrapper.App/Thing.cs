using EventStore.Client.Lite;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kubernetes.Bootstrapper.One.App
{
    public class Thing : AggregateBase<Guid>
    {
        public Thing()
        {
            EntityId = Guid.NewGuid();
        }

        public string Value { get;  set; }
        public override bool Equals(object obj)
        {
            return obj is Thing && obj.GetHashCode() == GetHashCode();
        }

        public override int GetHashCode()
        {
            return EntityId.GetHashCode();
        }


    }


}
