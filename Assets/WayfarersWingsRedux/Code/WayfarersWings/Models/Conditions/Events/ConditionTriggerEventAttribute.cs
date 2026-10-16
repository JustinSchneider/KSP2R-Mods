#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using System;

namespace WayfarersWings.Models.Conditions.Events
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class ConditionTriggerEventAttribute : Attribute
    {
        public readonly Type TriggerEventType;

        public ConditionTriggerEventAttribute(Type triggerEventType)
        {
            TriggerEventType = triggerEventType;
        }
    }
}
