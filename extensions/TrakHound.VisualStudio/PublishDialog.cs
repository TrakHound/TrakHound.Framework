// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.VisualStudio.Extensibility.UI;
using System.Runtime.Serialization;

namespace TrakHound.VisualStudio
{
    /// <summary>
    /// The dialog the Publish commands show : the Projects to include (Publish TrakHound Packages...) and the Publish Target to publish to
    /// (the UI is defined in PublishDialog.xaml, an Embedded Resource)
    /// </summary>
    internal class PublishDialog : RemoteUserControl
    {
        public PublishDialog(PublishDialogData data) : base(data) { }
    }

    /// <summary>
    /// The Data Context of the PublishDialog
    /// </summary>
    [DataContract]
    internal class PublishDialogData : NotifyPropertyChangedObject
    {
        private readonly PublishTargetChoices _fixedChoices;
        private bool _showTargets;
        private string _targetsSource;
        private string _targetsNote;
        private string _targetsHiddenNote;


        /// <summary>
        /// Select the Projects in a Build File and the Target they are published to. The Targets are those of the selected Projects
        /// that publish, and change as Projects are selected
        /// </summary>
        public PublishDialogData(string buildFilePath, IEnumerable<BuildFileProject> projects)
        {
            Title = "Select the Projects to Build and Publish";
            Subtitle = buildFilePath;
            ShowProjects = true;

            // Everything is selected to start with, the same as running 'th packages build'
            foreach (var project in projects) Projects.Add(new PublishDialogProject(this, project));

            SelectAllCommand = new AsyncCommand((parameter, cancellationToken) => SetSelectedAsync(true));
            SelectNoneCommand = new AsyncCommand((parameter, cancellationToken) => SetSelectedAsync(false));

            RefreshTargets();
        }

        /// <summary>
        /// Select the Target one Package is published to
        /// </summary>
        public PublishDialogData(string projectPath, PublishTargetChoices choices)
        {
            Title = $"Publish {Path.GetFileNameWithoutExtension(projectPath)}";
            Subtitle = projectPath;
            _fixedChoices = choices;

            SelectAllCommand = new AsyncCommand((parameter, cancellationToken) => Task.CompletedTask);
            SelectNoneCommand = SelectAllCommand;

            RefreshTargets();
        }


        [DataMember]
        public string Title { get; }

        [DataMember]
        public string Subtitle { get; }

        [DataMember]
        public bool ShowProjects { get; }

        [DataMember]
        public ObservableList<PublishDialogProject> Projects { get; } = new();

        [DataMember]
        public AsyncCommand SelectAllCommand { get; }

        [DataMember]
        public AsyncCommand SelectNoneCommand { get; }

        /// <summary>
        /// There are Targets to choose from (two or more)
        /// </summary>
        [DataMember]
        public bool ShowTargets
        {
            get => _showTargets;
            private set => SetProperty(ref _showTargets, value);
        }

        /// <summary>
        /// The file (or how many files) the Targets are from
        /// </summary>
        [DataMember]
        public string TargetsSource
        {
            get => _targetsSource;
            private set => SetProperty(ref _targetsSource, value);
        }

        /// <summary>
        /// Shown under the file the Targets are from when it hides one in a parent directory
        /// </summary>
        [DataMember]
        public string TargetsHiddenNote
        {
            get => _targetsHiddenNote;
            private set
            {
                if (SetProperty(ref _targetsHiddenNote, value)) RaiseNotifyPropertyChangedEvent(nameof(HasTargetsHiddenNote));
            }
        }

        [DataMember]
        public bool HasTargetsHiddenNote => !string.IsNullOrEmpty(_targetsHiddenNote);

        /// <summary>
        /// Shown instead of the Targets when the selected Projects are published without one
        /// </summary>
        [DataMember]
        public string TargetsNote
        {
            get => _targetsNote;
            private set => SetProperty(ref _targetsNote, value);
        }

        [DataMember]
        public ObservableList<PublishDialogTarget> Targets { get; } = new();


        /// <summary>
        /// The selected Projects (Publish TrakHound Packages...)
        /// </summary>
        public List<BuildFileProject> SelectedProjects => Projects.Where(o => o.IsSelected).Select(o => o.Project).ToList();

        /// <summary>
        /// The Target to pass as --target, or null to pass none (no Targets, or none in common, so each Package's defaultTarget is used).
        /// With one Target, that one
        /// </summary>
        public string SelectedTarget { get; private set; }


        private Task SetSelectedAsync(bool selected)
        {
            foreach (var project in Projects) project.SetSelected(selected);
            RefreshTargets();
            return Task.CompletedTask;
        }

        internal void OnTargetSelected(PublishDialogTarget selected)
        {
            foreach (var target in Targets.Where(o => o != selected)) target.SetSelected(false);
            SelectedTarget = selected.Name;
        }

        /// <summary>
        /// Update the Targets for the selected Projects, keeping the selected Target when it is still one of them
        /// </summary>
        internal void RefreshTargets()
        {
            var choices = _fixedChoices;
            string note = null;
            if (choices == null)
            {
                var publishing = SelectedProjects.Where(o => o.Publish).ToList();
                if (publishing.Count == 0)
                {
                    choices = PublishTargetChoices.None;
                    note = "None of the selected Projects publish (the Package files are only built)";
                }
                else if (publishing.Any(o => o.SetsServer))
                {
                    // The server the Build File sets replaces the Publish Target's Destinations, so the Target only gives the version and its settings.
                    // --target applies to every Project that publishes, so it isn't chosen here
                    choices = PublishTargetChoices.None;
                    note = "Published to the server the Build File sets (each Project's defaultTarget gives the version)";
                }
                else
                {
                    choices = PublishTargets.GetChoices(publishing.Select(o => o.Directory));
                    if (choices.Files.Count > 0 && choices.Targets.Count == 0) note = $"The selected Projects' {PublishTargets.Filename} files have no Target in common, so each one's defaultTarget is used";
                }
            }

            var previous = SelectedTarget;
            Targets.Clear();
            foreach (var target in choices.Targets) Targets.Add(new PublishDialogTarget(this, target, string.Equals(target.Name, choices.DefaultTarget, StringComparison.OrdinalIgnoreCase)));

            ShowTargets = Targets.Count > 1;
            TargetsSource = choices.Files.Count > 0 ? choices.Source : null;
            TargetsHiddenNote = choices.HiddenNote;
            TargetsNote = note;

            var selected = Targets.FirstOrDefault(o => string.Equals(o.Name, previous, StringComparison.OrdinalIgnoreCase))
                ?? Targets.FirstOrDefault(o => o.IsDefault)
                ?? Targets.FirstOrDefault();

            SelectedTarget = null;
            if (selected != null)
            {
                selected.SetSelected(true);
                OnTargetSelected(selected);
            }
        }
    }

    [DataContract]
    internal class PublishDialogProject : NotifyPropertyChangedObject
    {
        private readonly PublishDialogData _dialog;
        private bool _isSelected = true;


        public PublishDialogProject(PublishDialogData dialog, BuildFileProject project)
        {
            _dialog = dialog;
            Project = project;
        }


        public BuildFileProject Project { get; }

        [DataMember]
        public string Name => Project.Name;

        /// <summary>
        /// Shown beside the name of a Project that the Build File builds without publishing
        /// </summary>
        [DataMember]
        public string Note => Project.Publish ? string.Empty : "(builds the Package file only)";

        [DataMember]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value)) _dialog.RefreshTargets();
            }
        }

        /// <summary>
        /// Set without updating the Targets (Select All and Select None update them once)
        /// </summary>
        internal void SetSelected(bool value) => SetProperty(ref _isSelected, value, nameof(IsSelected));
    }

    [DataContract]
    internal class PublishDialogTarget : NotifyPropertyChangedObject
    {
        private readonly PublishDialogData _dialog;
        private bool _isSelected;


        public PublishDialogTarget(PublishDialogData dialog, PublishTarget target, bool isDefault)
        {
            _dialog = dialog;
            Name = target.Name;
            IsDefault = isDefault;

            Destinations = target.Destinations.Count > 0
                ? string.Join(Environment.NewLine, target.Destinations.Select(o => $"{o.PackageServer} | {o.Organization}"))
                : "(no Destinations)";

            var notes = new List<string>();
            notes.Add(!string.IsNullOrEmpty(target.VersionSuffix) ? $"Prerelease Version (-{target.VersionSuffix})" : "Version from the commit");
            if (target.AllowDirty) notes.Add("uncommitted changes allowed");
            if (target.RequireSigning) notes.Add("signed");
            foreach (var variable in target.Destinations.Select(o => o.ApiTokenVariable).Where(o => !string.IsNullOrEmpty(o)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                notes.Add($"Api Token from {variable}");
            }
            Notes = string.Join(" · ", notes);

            // The CLI runs with Visual Studio's environment, so a variable set after Visual Studio started isn't passed to it
            var missing = target.Destinations.Select(o => o.ApiTokenVariable)
                .Where(o => !string.IsNullOrEmpty(o) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(o)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (missing.Count > 0)
            {
                var setLater = missing.Any(o => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(o, EnvironmentVariableTarget.User)) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(o, EnvironmentVariableTarget.Machine)));
                Warning = setLater
                    ? $"{string.Join(", ", missing)} was set after Visual Studio started : Restart Visual Studio to publish to this Target"
                    : $"{string.Join(", ", missing)} isn't set : Publishing to this Target will fail";
            }
        }


        public string Name { get; }

        public bool IsDefault { get; }

        [DataMember]
        public string Label => IsDefault ? $"{Name} (default)" : Name;

        [DataMember]
        public string Destinations { get; }

        [DataMember]
        public string Notes { get; }

        [DataMember]
        public string Warning { get; }

        [DataMember]
        public bool HasWarning => !string.IsNullOrEmpty(Warning);

        [DataMember]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value) && value) _dialog.OnTargetSelected(this);
            }
        }

        internal void SetSelected(bool value) => SetProperty(ref _isSelected, value, nameof(IsSelected));
    }
}
