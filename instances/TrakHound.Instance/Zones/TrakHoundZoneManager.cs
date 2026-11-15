// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Apps;
using TrakHound.Configurations;
using TrakHound.Functions;
using TrakHound.Logging;
using TrakHound.Packages;
using TrakHound.Workers;

namespace TrakHound.Zones
{
    /// <summary>
    /// Reads the Zone Configurations and checks calls made by Modules against them. Calls that are denied (or that a Zone in Audit Mode
    /// would deny) are recorded as Activity, so they can be reviewed and granted.
    /// </summary>
    public class TrakHoundZoneManager : ITrakHoundZoneAuthorizer, IDisposable
    {
        private const int _maxActivity = 1000;
        private static readonly TimeSpan _notifyInterval = TimeSpan.FromSeconds(2);

        private readonly ITrakHoundConfigurationProfile _configurationProfile;
        private readonly ITrakHoundLogger _logger;
        private readonly Dictionary<string, TrakHoundZoneActivity> _activity = new Dictionary<string, TrakHoundZoneActivity>(); // Activity.Key => Activity
        private readonly object _lock = new object();
        private IReadOnlyList<TrakHoundZoneConfiguration> _zones = Array.Empty<TrakHoundZoneConfiguration>();
        private IReadOnlyDictionary<string, string> _memberGroups = new Dictionary<string, string>(); // Member ID => Group Reference ("group:{groupId}")

        // The Configuration Categories of Modules that can be Members (and be in a Group)
        private static readonly HashSet<string> _memberCategories = new HashSet<string>
        {
            TrakHoundApiConfiguration.ConfigurationCategory,
            TrakHoundAppConfiguration.ConfigurationCategory,
            TrakHoundFunctionConfiguration.ConfigurationCategory,
            TrakHoundWorkerConfiguration.ConfigurationCategory
        };


        /// <summary>
        /// Finds what the Packages of Zone Members can do outside of their Client (which a Zone can't restrict)
        /// </summary>
        public TrakHoundPackageCapabilityScanner Capabilities { get; }

        /// <summary>
        /// Raised when a Zone is added, changed or removed
        /// </summary>
        public event EventHandler ZonesUpdated;

        /// <summary>
        /// Raised when a call is denied (or would be denied by a Zone in Audit Mode)
        /// </summary>
        public event EventHandler<TrakHoundZoneActivity> ActivityRecorded;


        /// <summary>
        /// The Admin Resource a signed in Admin needs "write" on to change these Zones from the Admin Interface (MoveMember and Grant change
        /// them through Save). Changes made outside an Admin's Circuit aren't checked
        /// </summary>
        public string AdminResource { get; set; } = TrakHound.Instance.Security.TrakHoundAdminSecurity.Zones;


        public TrakHoundZoneManager(ITrakHoundConfigurationProfile configurationProfile, ITrakHoundLogProvider logProvider, TrakHoundPackageManager packageManager = null)
        {
            _configurationProfile = configurationProfile;
            _logger = logProvider?.GetLoggerByName("zones");
            Capabilities = new TrakHoundPackageCapabilityScanner(packageManager);

            if (_configurationProfile != null)
            {
                _configurationProfile.ConfigurationAdded += ConfigurationChanged;
                _configurationProfile.ConfigurationRemoved += ConfigurationChanged;
            }

            LoadZones();
            LoadMemberGroups();
        }

        public void Dispose()
        {
            if (_configurationProfile != null)
            {
                _configurationProfile.ConfigurationAdded -= ConfigurationChanged;
                _configurationProfile.ConfigurationRemoved -= ConfigurationChanged;
            }
        }


        #region "Zones"

        /// <summary>
        /// Gets every Zone (ordered by ID)
        /// </summary>
        public IEnumerable<TrakHoundZoneConfiguration> GetZones() => _zones;

        public TrakHoundZoneConfiguration GetZone(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId)) return null;
            return _zones.FirstOrDefault(o => string.Equals(o.Id, zoneId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the Zone the Member belongs to (null if it is not in a Zone). A Zone that lists the Member (by its ID or a pattern) comes before
        /// a Zone that lists its Group ("group:{groupId}"). A Member listed in more than one Zone belongs to the first (by ID)
        /// </summary>
        public TrakHoundZoneConfiguration GetMemberZone(string memberId)
        {
            if (string.IsNullOrEmpty(memberId)) return null;

            var zone = _zones.FirstOrDefault(o => IsListed(o.Members, memberId));
            if (zone != null) return zone;

            var groupReference = GetMemberGroup(memberId);
            return groupReference != null ? _zones.FirstOrDefault(o => IsListed(o.Members, groupReference)) : null;
        }

        /// <summary>
        /// Gets every Zone the Member is listed in, by its ID, a pattern or its Group (used to warn about a Member listed in more than one Zone)
        /// </summary>
        public IEnumerable<TrakHoundZoneConfiguration> GetMemberZones(string memberId)
        {
            if (string.IsNullOrEmpty(memberId)) return Enumerable.Empty<TrakHoundZoneConfiguration>();

            var groupReference = GetMemberGroup(memberId);
            return _zones.Where(o => IsListed(o.Members, memberId) || (groupReference != null && IsListed(o.Members, groupReference))).ToList();
        }

        /// <summary>
        /// Gets the Group Reference ("group:{groupId}") of the Module with the Member ID (null if it isn't in a Group)
        /// </summary>
        public string GetMemberGroup(string memberId)
        {
            return !string.IsNullOrEmpty(memberId) ? _memberGroups.GetValueOrDefault(memberId) : null;
        }

        private static bool IsListed(IEnumerable<string> patterns, string id)
        {
            return !patterns.IsNullOrEmpty() && patterns.Any(o => TrakHoundZoneResources.IsPatternMatch(o, id));
        }

        /// <summary>
        /// Gets the Router that the Zone of the caller sends every Service call to (null if the Zone doesn't set one)
        /// </summary>
        public string GetRouterId(TrakHoundApiRequestSource caller)
        {
            var zone = GetMemberZone(TrakHoundZoneResources.GetMemberId(caller));
            return !string.IsNullOrEmpty(zone?.RouterId) ? zone.RouterId : null;
        }

        /// <summary>
        /// Adds (or replaces) the Zone and saves it to the Configuration Profile
        /// </summary>
        public bool Save(TrakHoundZoneConfiguration zone)
        {
            TrakHound.Instance.Security.TrakHoundAdminContext.DemandWrite(AdminResource);

            if (zone != null && !string.IsNullOrEmpty(zone.Id) && _configurationProfile != null)
            {
                _configurationProfile.Add(zone, true);

                // Apply the change now (the Profile's change event may be raised later)
                LoadZones();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Moves a Member (a Member ID or pattern) between Zones: it is removed from <paramref name="fromZoneId"/> (if set) and added to
        /// <paramref name="toZoneId"/> (if set). A Module that is listed in another Zone by its own ID is removed from that Zone too, so it only belongs to one.
        /// Returns false if a Zone isn't found or nothing changed
        /// </summary>
        public bool MoveMember(string member, string fromZoneId, string toZoneId)
        {
            if (string.IsNullOrEmpty(member)) return false;
            if (string.Equals(fromZoneId, toZoneId, StringComparison.OrdinalIgnoreCase)) return false;

            var toZone = GetZone(toZoneId);
            if (!string.IsNullOrEmpty(toZoneId) && toZone == null) return false;

            // Remove it from every other Zone that lists it (the Zone it was dragged from, and any Zone that also lists it)
            var changed = false;
            foreach (var zone in _zones.Where(o => o != toZone && (o.Members?.Contains(member, StringComparer.OrdinalIgnoreCase) ?? false)).ToList())
            {
                if (!string.IsNullOrEmpty(fromZoneId) && !string.Equals(zone.Id, fromZoneId, StringComparison.OrdinalIgnoreCase) && toZone == null) continue;

                var updated = Copy(zone);
                updated.Members = zone.Members.Where(o => !string.Equals(o, member, StringComparison.OrdinalIgnoreCase)).ToList();
                changed |= Save(updated);
            }

            if (toZone != null && !(toZone.Members?.Contains(member, StringComparer.OrdinalIgnoreCase) ?? false))
            {
                var updated = Copy(toZone);
                updated.Members = (toZone.Members ?? Enumerable.Empty<string>()).Append(member).ToList();
                changed |= Save(updated);
            }

            return changed;
        }

        /// <summary>
        /// Removes the Zone from the Configuration Profile (and deletes its file)
        /// </summary>
        public bool Remove(string zoneId)
        {
            TrakHound.Instance.Security.TrakHoundAdminContext.DemandWrite(AdminResource);

            if (!string.IsNullOrEmpty(zoneId) && _configurationProfile != null)
            {
                _configurationProfile.Remove(TrakHoundZoneConfiguration.ConfigurationCategory, zoneId);
                lock (_lock)
                {
                    foreach (var key in _activity.Where(o => o.Value.ZoneId == zoneId).Select(o => o.Key).ToList()) _activity.Remove(key);
                }
                return true;
            }

            return false;
        }

        private void ConfigurationChanged(object sender, ITrakHoundConfiguration configuration)
        {
            if (configuration?.Category == TrakHoundZoneConfiguration.ConfigurationCategory)
            {
                LoadZones();
                ZonesUpdated?.Invoke(this, EventArgs.Empty);
            }
            else if (configuration != null && _memberCategories.Contains(configuration.Category))
            {
                // A Module that joins or leaves a Group can change which Zone it is in
                if (LoadMemberGroups()) ZonesUpdated?.Invoke(this, EventArgs.Empty);
            }
        }

        private void LoadZones()
        {
            var zones = _configurationProfile?.Get<TrakHoundZoneConfiguration>(TrakHoundZoneConfiguration.ConfigurationCategory);
            _zones = zones?.Where(o => !string.IsNullOrEmpty(o.Id)).OrderBy(o => o.Id, StringComparer.OrdinalIgnoreCase).ToList() ?? new List<TrakHoundZoneConfiguration>();
        }

        /// <summary>
        /// Reads the Group of every Module (kept so a Zone check doesn't read the Configurations). Returns true if any Module's Group changed
        /// </summary>
        private bool LoadMemberGroups()
        {
            var memberGroups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            void Add(string prefix, string configurationId, string groupId)
            {
                var memberId = TrakHoundZoneResources.GetMemberId(prefix, configurationId);
                var groupReference = TrakHoundZoneResources.GetGroupReference(groupId);
                if (memberId != null && groupReference != null) memberGroups[memberId] = groupReference;
            }

            if (_configurationProfile != null)
            {
                foreach (var o in _configurationProfile.Get<ITrakHoundApiConfiguration>(TrakHoundApiConfiguration.ConfigurationCategory)) Add(TrakHoundZoneResources.ApiPrefix, o.Id, o.GroupId);
                foreach (var o in _configurationProfile.Get<ITrakHoundAppConfiguration>(TrakHoundAppConfiguration.ConfigurationCategory)) Add(TrakHoundZoneResources.AppPrefix, o.Id, o.GroupId);
                foreach (var o in _configurationProfile.Get<ITrakHoundFunctionConfiguration>(TrakHoundFunctionConfiguration.ConfigurationCategory)) Add(TrakHoundZoneResources.FunctionPrefix, o.Id, o.GroupId);
                foreach (var o in _configurationProfile.Get<ITrakHoundWorkerConfiguration>(TrakHoundWorkerConfiguration.ConfigurationCategory)) Add(TrakHoundZoneResources.WorkerPrefix, o.Id, o.GroupId);
            }

            var previous = _memberGroups;
            _memberGroups = memberGroups;
            return previous.Count != memberGroups.Count || previous.Any(o => memberGroups.GetValueOrDefault(o.Key) != o.Value);
        }

        #endregion

        #region "Authorization"

        /// <inheritdoc/>
        public TrakHoundZoneDecision Authorize(TrakHoundApiRequestSource caller, string targetMemberId, string resourceId, string permission)
        {
            var callerMemberId = TrakHoundZoneResources.GetMemberId(caller);
            var result = Evaluate(callerMemberId, targetMemberId, resourceId, permission);

            if (result.Zone != null)
            {
                Record(result, caller, callerMemberId, targetMemberId, resourceId, permission);
                return result.Decision;
            }

            return TrakHoundZoneDecision.Allowed;
        }

        /// <summary>
        /// Checks whether the Member can access the Resource (without recording Activity)
        /// </summary>
        public TrakHoundZoneEvaluation Evaluate(string callerMemberId, string targetMemberId, string resourceId, string permission)
        {
            // Calls that are not made by a Module (ex. the Instance, the Admin Interface or an HTTP Request) are not governed by a Zone
            if (string.IsNullOrEmpty(callerMemberId) || string.IsNullOrEmpty(resourceId)) return TrakHoundZoneEvaluation.NotGoverned;

            // A Module can always call itself
            if (targetMemberId != null && string.Equals(callerMemberId, targetMemberId, StringComparison.OrdinalIgnoreCase)) return TrakHoundZoneEvaluation.NotGoverned;

            var callerZone = GetMemberZone(callerMemberId);
            var targetZone = GetMemberZone(targetMemberId);
            var sameZone = callerZone != null && targetZone != null && callerZone.Id == targetZone.Id;

            // Outbound : what the Members of the caller's Zone can access
            if (callerZone != null && !(sameZone && callerZone.AllowSameZone))
            {
                var matched = callerZone.Rules?.FirstOrDefault(o => TrakHoundZoneResources.IsRuleMatch(o, resourceId, permission));
                if (matched == null)
                {
                    var message = $"Zone ({callerZone.Id}) does not allow ({callerMemberId}) to access ({resourceId}) with Permission ({permission})";
                    return TrakHoundZoneEvaluation.Deny(callerZone, TrakHoundZoneDirection.Outbound, message);
                }
            }

            // Inbound : which Modules can call the Members of the target's Zone
            if (targetZone != null && !sameZone && !targetZone.AllowedCallers.IsNullOrEmpty())
            {
                var callerZoneReference = TrakHoundZoneResources.GetZoneReference(callerZone?.Id);
                var callerGroupReference = GetMemberGroup(callerMemberId);
                var allowed = targetZone.AllowedCallers.Any(o =>
                    TrakHoundZoneResources.IsPatternMatch(o, callerMemberId) ||
                    (callerZoneReference != null && TrakHoundZoneResources.IsPatternMatch(o, callerZoneReference)) ||
                    (callerGroupReference != null && TrakHoundZoneResources.IsPatternMatch(o, callerGroupReference)));
                if (!allowed)
                {
                    var message = $"Zone ({targetZone.Id}) does not allow ({callerMemberId}) to call ({targetMemberId})";
                    return TrakHoundZoneEvaluation.Deny(targetZone, TrakHoundZoneDirection.Inbound, message);
                }
            }

            return callerZone != null || targetZone != null ? TrakHoundZoneEvaluation.Allow(callerZone ?? targetZone) : TrakHoundZoneEvaluation.NotGoverned;
        }

        #endregion

        #region "Activity"

        /// <summary>
        /// Gets the recorded Activity (calls that were denied, or would be denied in Audit Mode), most recent first
        /// </summary>
        public IEnumerable<TrakHoundZoneActivity> GetActivity(string zoneId = null)
        {
            lock (_lock)
            {
                return _activity.Values
                    .Where(o => zoneId == null || o.ZoneId == zoneId)
                    .OrderByDescending(o => o.LastSeen)
                    .Select(o => o.Clone())
                    .ToList();
            }
        }

        /// <summary>
        /// Removes a recorded Activity item (ex. after it is Granted or dismissed)
        /// </summary>
        public void ClearActivity(string key)
        {
            TrakHound.Instance.Security.TrakHoundAdminContext.DemandWrite(AdminResource);

            if (key != null) lock (_lock) _activity.Remove(key);
        }

        /// <summary>
        /// Removes every recorded Activity item for the Zone (all Zones if null)
        /// </summary>
        public void ClearAllActivity(string zoneId = null)
        {
            TrakHound.Instance.Security.TrakHoundAdminContext.DemandWrite(AdminResource);

            lock (_lock)
            {
                foreach (var key in _activity.Where(o => zoneId == null || o.Value.ZoneId == zoneId).Select(o => o.Key).ToList()) _activity.Remove(key);
            }
        }

        /// <summary>
        /// Changes the Zone so the Activity is allowed: an Outbound call adds a Rule for the Resource (or the pattern given), and an Inbound call adds the caller to AllowedCallers
        /// </summary>
        public bool Grant(TrakHoundZoneActivity activity, string resourcePattern = null)
        {
            var zone = GetZone(activity?.ZoneId);
            if (zone == null) return false;

            var updated = Copy(zone);
            if (activity.Direction == TrakHoundZoneDirection.Outbound)
            {
                var rules = updated.Rules?.ToList() ?? new List<TrakHoundZoneRule>();
                rules.Add(new TrakHoundZoneRule
                {
                    Resource = !string.IsNullOrEmpty(resourcePattern) ? resourcePattern : activity.ResourceId,
                    Permissions = !string.IsNullOrEmpty(activity.Permission) ? new[] { activity.Permission } : null,
                    Description = $"Granted for {activity.CallerName ?? activity.CallerId}"
                });
                updated.Rules = rules;
            }
            else
            {
                var callers = updated.AllowedCallers?.ToList() ?? new List<string>();
                if (!callers.Contains(activity.CallerId, StringComparer.OrdinalIgnoreCase)) callers.Add(activity.CallerId);
                updated.AllowedCallers = callers;
            }

            if (Save(updated))
            {
                ClearActivity(activity.Key);

                // The change may also allow other recorded calls (ex. the same Resource called by another Member)
                ClearAllowedActivity(zone.Id);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Removes the recorded Activity for the Zone that the current Zones now allow
        /// </summary>
        private void ClearAllowedActivity(string zoneId)
        {
            lock (_lock)
            {
                var allowed = _activity.Values
                    .Where(o => o.ZoneId == zoneId)
                    .Where(o => Evaluate(o.CallerId, o.TargetId, o.ResourceId, o.Permission).Direction == null)
                    .Select(o => o.Key)
                    .ToList();

                foreach (var key in allowed) _activity.Remove(key);
            }
        }

        private void Record(TrakHoundZoneEvaluation result, TrakHoundApiRequestSource caller, string callerMemberId, string targetMemberId, string resourceId, string permission)
        {
            if (result.Direction == null) return; // Allowed

            var key = $"{result.Zone.Id}|{result.Direction}|{callerMemberId}|{resourceId}|{permission}".ToLowerInvariant();
            var enforced = result.Decision.IsAllowed == false;
            var now = DateTime.UtcNow;
            var isNew = false;
            var notify = false;

            TrakHoundZoneActivity activity;
            lock (_lock)
            {
                activity = _activity.GetValueOrDefault(key);
                if (activity == null)
                {
                    // Keep the most recent items
                    if (_activity.Count >= _maxActivity)
                    {
                        var oldest = _activity.Values.OrderBy(o => o.LastSeen).First();
                        _activity.Remove(oldest.Key);
                    }

                    activity = new TrakHoundZoneActivity
                    {
                        Key = key,
                        ZoneId = result.Zone.Id,
                        Direction = result.Direction.Value,
                        CallerId = callerMemberId,
                        CallerName = caller?.Name,
                        TargetId = targetMemberId,
                        ResourceId = resourceId,
                        Permission = permission,
                        FirstSeen = now
                    };
                    _activity.Add(key, activity);
                    isNew = true;
                }

                activity.Enforced = enforced;
                activity.Message = result.Decision.Message;
                activity.Count++;
                activity.LastSeen = now;

                // Repeated calls are only raised every few seconds (so a busy Module doesn't flood the Admin Interface)
                notify = isNew || now - activity.LastNotified > _notifyInterval;
                if (notify) activity.LastNotified = now;

                activity = activity.Clone();
            }

            // Log the first occurrence as a Warning (repeats are logged at Debug so a busy Module doesn't flood the log)
            var logMessage = $"{(enforced ? "Zone Access Denied" : "Zone Audit (Would Deny)")} : {result.Decision.Message}";
            if (isNew) _logger?.LogWarning(logMessage);
            else _logger?.LogDebug(logMessage);

            if (notify) ActivityRecorded?.Invoke(this, activity);
        }

        #endregion

        /// <summary>
        /// Creates a copy of the Zone (so the loaded Configuration isn't changed until it is Saved)
        /// </summary>
        public static TrakHoundZoneConfiguration Copy(TrakHoundZoneConfiguration zone)
        {
            if (zone == null) return null;

            return new TrakHoundZoneConfiguration
            {
                Id = zone.Id,
                Name = zone.Name,
                Description = zone.Description,
                Mode = zone.Mode,
                Members = zone.Members?.ToList(),
                AllowSameZone = zone.AllowSameZone,
                Rules = zone.Rules?.Select(o => new TrakHoundZoneRule { Resource = o.Resource, Permissions = o.Permissions?.ToList(), Description = o.Description }).ToList(),
                AllowedCallers = zone.AllowedCallers?.ToList(),
                RouterId = zone.RouterId,
                Path = zone.Path
            };
        }
    }
}
