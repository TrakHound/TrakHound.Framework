using System;

namespace TrakHound.Services.Tables.Attributes
{
    /// <summary>
    /// Marks a property whose Column is compared in a generated Merge. When any property has this attribute,
    /// matched rows are only updated if one of these Columns changed (otherwise matched rows are always updated)
    /// </summary>
    public class UpdateAttribute : Attribute
    {
        /// <summary>
        /// Creates an UpdateAttribute
        /// </summary>
        public UpdateAttribute() { }
    }
}
