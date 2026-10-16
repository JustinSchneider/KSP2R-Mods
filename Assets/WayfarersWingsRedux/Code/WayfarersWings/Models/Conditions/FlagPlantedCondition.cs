#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KSP.Messages;
using WayfarersWings.Models.Conditions.Events;
using WayfarersWings.Models.Wings;


// TODO We could do something (with GetTriggers) to make this work with any event

namespace WayfarersWings.Models.Conditions
{
    [ConditionTriggerEvent(typeof(FlagPlantedMessage))]
    public class FlagPlantedCondition : BaseCondition
    {
        public override bool IsValid(Transaction transaction)
        {
            return transaction?.Message is FlagPlantedMessage;
        }
    }
}
