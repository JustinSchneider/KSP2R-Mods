#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WayfarersWings.Models.Session.Json
{
    public interface IJsonSaved
    {
        public void OnAfterGameLoad();
        public void OnBeforeGameSave();
    }
}
