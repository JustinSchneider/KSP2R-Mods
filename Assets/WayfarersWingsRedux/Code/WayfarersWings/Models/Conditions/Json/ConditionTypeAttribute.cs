#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// An attribute that can be used to associate a condition type with a discriminator.
/// Can be used multiple times on the same class.
/// </summary>

namespace WayfarersWings.Models.Conditions.Json
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class ConditionTypeAttribute : Attribute
    {
        public string Discriminator;
        public Type ConditionType;

        public ConditionTypeAttribute(string discriminator, Type conditionType)
        {
            Discriminator = discriminator;
            ConditionType = conditionType;
        }
    }
}
