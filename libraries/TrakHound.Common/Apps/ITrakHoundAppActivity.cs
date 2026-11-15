// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Apps
{
    /// <summary>
    /// Lets an App Page report its state to the Instance, so an admin can tell whether it is safe to reload the page
    /// (shown on the Instance's App Connections). Cascaded to App Pages as "AppActivity" :
    /// <code>[CascadingParameter(Name = "AppActivity")] public ITrakHoundAppActivity AppActivity { get; set; }</code>
    /// </summary>
    public interface ITrakHoundAppActivity
    {
        /// <summary>
        /// Declare whether the current Page has unsaved changes (ex. true when a form is edited, false once it is saved).
        /// It applies until the connection navigates to another Page. Pages that don't declare it are treated as having
        /// unsaved changes once any input has been typed on them.
        /// </summary>
        void SetUnsavedChanges(bool hasUnsavedChanges);
    }
}
