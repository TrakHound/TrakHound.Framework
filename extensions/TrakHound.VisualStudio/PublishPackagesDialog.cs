// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.VisualStudio.Extensibility.UI;
using System.Runtime.Serialization;

namespace TrakHound.VisualStudio
{
    /// <summary>
    /// The dialog that selects which of a Build File's Projects to Publish (the UI is defined in PublishPackagesDialog.xaml, an Embedded Resource)
    /// </summary>
    internal class PublishPackagesDialog : RemoteUserControl
    {
        public PublishPackagesDialog(PublishPackagesDialogData data) : base(data) { }
    }

    /// <summary>
    /// The Data Context of the PublishPackagesDialog
    /// </summary>
    [DataContract]
    internal class PublishPackagesDialogData : NotifyPropertyChangedObject
    {
        public PublishPackagesDialogData(string buildFilePath, IEnumerable<BuildFileProject> projects)
        {
            BuildFilePath = buildFilePath;

            // Everything is selected to start with, the same as running 'th packages build'
            foreach (var project in projects) Projects.Add(new PublishPackagesDialogItem(project));

            SelectAllCommand = new AsyncCommand((parameter, cancellationToken) => SetSelectedAsync(true));
            SelectNoneCommand = new AsyncCommand((parameter, cancellationToken) => SetSelectedAsync(false));
        }


        [DataMember]
        public string BuildFilePath { get; }

        [DataMember]
        public ObservableList<PublishPackagesDialogItem> Projects { get; } = new();

        [DataMember]
        public AsyncCommand SelectAllCommand { get; }

        [DataMember]
        public AsyncCommand SelectNoneCommand { get; }


        private Task SetSelectedAsync(bool selected)
        {
            foreach (var project in Projects) project.IsSelected = selected;
            return Task.CompletedTask;
        }
    }

    [DataContract]
    internal class PublishPackagesDialogItem : NotifyPropertyChangedObject
    {
        private bool _isSelected = true;


        public PublishPackagesDialogItem(BuildFileProject project)
        {
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
            set => SetProperty(ref _isSelected, value);
        }
    }
}
