using System;

namespace TrakHound.Services.Tables.Attributes
{
    /// <summary>
    /// Marks the property's Column as NOT NULL even if the property type allows null
    /// </summary>
    public class NotNullAttribute : Attribute { }
}
