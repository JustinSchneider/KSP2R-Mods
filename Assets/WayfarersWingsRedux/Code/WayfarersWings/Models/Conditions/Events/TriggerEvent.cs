#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using KSP.Messages;
using Newtonsoft.Json;
using WayfarersWings.Utility;

namespace WayfarersWings.Models.Conditions.Events
{
    [Serializable]
    public class TriggerEvent
    {
        public string? eventName;
        public Type eventType = null!;

        [OnDeserialized]
        private void OnDeserialize(StreamingContext context)
        {
            if (eventName == null)
                throw new InvalidCastException("Event name must be specified");

            var eventFullName = eventName.StartsWith("KSP.Messages.") ? eventName : $"KSP.Messages.{eventName}";
            // Resolve through the assembly that actually defines the messages; since 26w42c that's Ksp2Redux,
            // with Assembly-CSharp only forwarding to it.
            eventType = typeof(MessageCenterMessage).Assembly.GetType(eventFullName) ??
                        throw new InvalidCastException($"Could not find event type '{eventFullName}'");
        }
    }
}
