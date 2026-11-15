// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.VisualStudio.Extensibility.UI;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Text;

namespace TrakHound.VisualStudio
{
    /// <summary>
    /// The output of the current (or last) Publish. Shared (as a singleton) by the PublishPackageCommand and the PublishToolWindow,
    /// and used as the Data Context of the PublishToolWindowControl
    /// </summary>
    [DataContract]
    internal class PublishLog : NotifyPropertyChangedObject
    {
        // Each Output update sends the whole text to Visual Studio, so lines are batched
        private static readonly TimeSpan _updateInterval = TimeSpan.FromMilliseconds(200);

        private readonly object _lock = new();
        private readonly StringBuilder _output = new();
        private readonly Stopwatch _lastUpdate = new();
        private string _outputText = string.Empty;
        private string _status = "No TrakHound Package has been Published";


        [DataMember]
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        [DataMember]
        public string Output
        {
            get => _outputText;
            private set => SetProperty(ref _outputText, value);
        }


        public void Start(string status)
        {
            lock (_lock)
            {
                _output.Clear();
                _lastUpdate.Restart();
                Output = string.Empty;
                Status = status;
            }
        }

        public void AppendLine(string line)
        {
            lock (_lock)
            {
                _output.AppendLine(line);

                if (_lastUpdate.Elapsed >= _updateInterval)
                {
                    Output = _output.ToString();
                    _lastUpdate.Restart();
                }
            }
        }

        public void Complete(string status)
        {
            lock (_lock)
            {
                Output = _output.ToString();
                Status = status;
            }
        }
    }
}
