using System;

namespace TrakHound.Services.Tables.Attributes
{
    /// <summary>
    /// Creates the Table's Primary Key with IGNORE_DUP_KEY = ON so that Inserts with an existing Key are ignored instead of failing
    /// </summary>
    public class IgnoreDuplicateKeyAttribute : Attribute { }
}
