// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities;
using Mafi.Core.Mods;
using Mafi.Core.Notifications;
using Mafi.Core.Prototypes;

namespace AutoTerrainDesignations
{
    internal static class AtdNotifications
    {
        internal static readonly EntityNotificationProto<string>.ID RampAccessFailedId =
            new EntityNotificationProto<string>.ID("ATD_RampAccessFailed");
        internal static readonly EntityNotificationProto<string>.ID RampAccessTruncatedId =
            new EntityNotificationProto<string>.ID("ATD_RampAccessTruncated");
        internal static readonly EntityNotificationProto<string>.ID RampAccessNotAccessibleId =
            new EntityNotificationProto<string>.ID("ATD_RampAccessNotAccessible");
        internal static readonly EntityNotificationProto<string>.ID RampAccessSnapshotTooLargeId =
            new EntityNotificationProto<string>.ID("ATD_RampAccessSnapshotTooLarge");
        internal static readonly EntityNotificationProto<string>.ID FarmingCompleteId =
            new EntityNotificationProto<string>.ID("ATD_FarmingComplete");
        internal static readonly EntityNotificationProto<string>.ID ExcavatorCompletedId =
            new EntityNotificationProto<string>.ID("ATD_ExcavatorCompleted");
        internal static readonly EntityNotificationProto<string>.ID DebrisCleanupNoneFoundId =
            new EntityNotificationProto<string>.ID("ATD_DebrisCleanupNoneFound");
        internal static readonly EntityNotificationProto<string>.ID DebrisCleanupNoneReachableId =
            new EntityNotificationProto<string>.ID("ATD_DebrisCleanupNoneReachable");

        private static readonly HashSet<string> s_protoIds = new HashSet<string>
        {
            "ATD_RampAccessWarning",
            RampAccessFailedId.Value,
            RampAccessTruncatedId.Value,
            RampAccessNotAccessibleId.Value,
            RampAccessSnapshotTooLargeId.Value,
            FarmingCompleteId.Value,
            ExcavatorCompletedId.Value,
            DebrisCleanupNoneFoundId.Value,
            DebrisCleanupNoneReachableId.Value,
        };

        internal static void RegisterPrototypes(ProtoRegistrator registrator)
        {
            RegisterLocalizedEntity(
                registrator,
                () => AtdLocalization.NotifRampFailed.TranslatedString,
                RampAccessFailedId,
                NotificationType.Continuous,
                NotificationStyle.Warning,
                "Assets/Unity/UserInterface/EntityIcons/Designation.png",
                "Assets/Unity/UserInterface/EntityIcons/Warning.png");
            RegisterLocalizedEntity(
                registrator,
                () => AtdLocalization.NotifRampTruncated.TranslatedString,
                RampAccessTruncatedId,
                NotificationType.Continuous,
                NotificationStyle.Warning,
                "Assets/Unity/UserInterface/EntityIcons/Designation.png",
                "Assets/Unity/UserInterface/EntityIcons/Warning.png");
            RegisterLocalizedEntity(
                registrator,
                () => AtdLocalization.NotifRampNotAccessible.TranslatedString,
                RampAccessNotAccessibleId,
                NotificationType.Continuous,
                NotificationStyle.Warning,
                "Assets/Unity/UserInterface/EntityIcons/Designation.png",
                "Assets/Unity/UserInterface/EntityIcons/Warning.png");
            RegisterLocalizedEntity(
                registrator,
                () => AtdLocalization.NotifRampSnapshotTooLarge.TranslatedString,
                RampAccessSnapshotTooLargeId,
                NotificationType.Continuous,
                NotificationStyle.Warning,
                "Assets/Unity/UserInterface/EntityIcons/Designation.png",
                "Assets/Unity/UserInterface/EntityIcons/Warning.png");
            RegisterLocalizedEntity(
                registrator,
                () => AtdLocalization.NotifFarmingComplete.TranslatedString,
                FarmingCompleteId,
                NotificationType.OneTimeOnly,
                NotificationStyle.Success,
                "Assets/Unity/UserInterface/EntityIcons/Designation.png",
                timeToLive: Duration.FromSec(20));
            RegisterSuccessFormatted(
                registrator,
                () => AtdLocalization.NotifExcavatorCompleted.TranslatedString,
                ExcavatorCompletedId,
                "Assets/Unity/UserInterface/Toolbar/Mining.svg");
            RegisterLocalizedEntity(
                registrator,
                () => AtdLocalization.NotifDebrisCleanupNoneFound.TranslatedString,
                DebrisCleanupNoneFoundId,
                NotificationType.OneTimeOnly,
                NotificationStyle.Success,
                "Assets/Unity/UserInterface/Toolbar/Sweep.svg",
                timeToLive: Duration.FromSec(20));
            RegisterLocalizedEntity(
                registrator,
                () => AtdLocalization.NotifDebrisCleanupNoneReachable.TranslatedString,
                DebrisCleanupNoneReachableId,
                NotificationType.OneTimeOnly,
                NotificationStyle.Warning,
                "Assets/Unity/UserInterface/Toolbar/Sweep.svg",
                timeToLive: Duration.FromSec(20));
        }

        private static void RegisterLocalizedEntity(
            ProtoRegistrator registrator,
            Func<string> messageProvider,
            EntityNotificationProto<string>.ID id,
            NotificationType type,
            NotificationStyle style,
            string iconPath,
            string? entityIconPath = null,
            Duration timeToLive = default(Duration))
        {
            var state = registrator.NotificationProtoBuilder
                .StartFormatted("{entity}", id)
                .SetType(type)
                .SetStyle(style)
                .SetTimeToLive(timeToLive)
                .MuteAudio()
                .AddIcon(iconPath);
            if (entityIconPath != null)
                state.AddEntityIcon(entityIconPath);
            state
                .SetMessageFormatter((entityTitle, _) =>
                    FormatEntityMessage(messageProvider(), entityTitle))
                .BuildAndAdd(doNotRequireEntityIcon: entityIconPath == null);
        }

        private static void RegisterSuccessFormatted<T>(
            ProtoRegistrator registrator,
            Func<string> messageProvider,
            EntityNotificationProto<T>.ID id,
            string iconPath = "Assets/Unity/UserInterface/EntityIcons/Designation.png")
        {
            registrator.NotificationProtoBuilder
                // Keep the entity placeholder as the formatter input. The game expands it
                // before invoking SetMessageFormatter; passing the localized template here
                // would make the callback receive the whole (stale) English message.
                .StartFormatted("{entity}", id)
                .SetType(NotificationType.OneTimeOnly)
                .SetStyle(NotificationStyle.Success)
                .SetTimeToLive(Duration.FromSec(20))
                .MuteAudio()
                .AddIcon(iconPath)
                .SetMessageFormatter((entityTitle, parameter) =>
                    FormatMessage(messageProvider(), entityTitle, parameter))
                .BuildAndAdd(doNotRequireEntityIcon: true);
        }

        private static string FormatEntityMessage(string message, string entityTitle)
        {
            return message.Replace("{entity}", entityTitle);
        }

        private static string FormatMessage<T>(string message, string entityTitle, T parameter)
        {
            object? boxedParameter = parameter;
            return message
                .Replace("{entity}", entityTitle)
                .Replace("{0}", boxedParameter?.ToString() ?? string.Empty);
        }

        internal static bool IsAtdProto(NotificationProto proto)
        {
            return s_protoIds.Contains(proto.Id.Value);
        }
    }

    public static partial class AutoDepthDesignation
    {
        private enum TransientNotificationKind
        {
            RampAccessFailed,
            RampAccessTruncated,
            RampAccessNotAccessible,
            RampAccessSnapshotTooLarge,
        }

        private readonly struct TransientNotificationKey
        {
            public readonly EntityId EntityId;
            public readonly TransientNotificationKind Kind;

            public TransientNotificationKey(EntityId entityId, TransientNotificationKind kind)
            {
                EntityId = entityId;
                Kind = kind;
            }

            public override bool Equals(object? obj)
            {
                return obj is TransientNotificationKey other
                    && EntityId.Equals(other.EntityId)
                    && Kind == other.Kind;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (EntityId.GetHashCode() * 397) ^ (int)Kind;
                }
            }
        }

        private static INotificationsManager? s_notificationsManager;
        private static EntityNotificationProto<string>? s_rampAccessFailedNotificationProto;
        private static EntityNotificationProto<string>? s_rampAccessTruncatedNotificationProto;
        private static EntityNotificationProto<string>? s_rampAccessNotAccessibleNotificationProto;
        private static EntityNotificationProto<string>? s_rampAccessSnapshotTooLargeNotificationProto;
        private static EntityNotificationProto<string>? s_farmingCompleteNotificationProto;
        private static EntityNotificationProto<string>? s_excavatorCompletedNotificationProto;
        private static EntityNotificationProto<string>? s_debrisCleanupNoneFoundNotificationProto;
        private static EntityNotificationProto<string>? s_debrisCleanupNoneReachableNotificationProto;
        private static readonly Dictionary<TransientNotificationKey, NotificationId> s_transientNotificationsByKey =
            new Dictionary<TransientNotificationKey, NotificationId>();

        private static void InitializeTransientNotifications(INotificationsManager? notificationsManager, ProtosDb protosDb)
        {
            s_notificationsManager = notificationsManager;

            TryInitializeTransientNotificationProto(protosDb, AtdNotifications.RampAccessFailedId, ref s_rampAccessFailedNotificationProto);
            TryInitializeTransientNotificationProto(protosDb, AtdNotifications.RampAccessTruncatedId, ref s_rampAccessTruncatedNotificationProto);
            TryInitializeTransientNotificationProto(protosDb, AtdNotifications.RampAccessNotAccessibleId, ref s_rampAccessNotAccessibleNotificationProto);
            TryInitializeTransientNotificationProto(protosDb, AtdNotifications.RampAccessSnapshotTooLargeId, ref s_rampAccessSnapshotTooLargeNotificationProto);
            TryInitializeTransientNotificationProto(protosDb, AtdNotifications.FarmingCompleteId, ref s_farmingCompleteNotificationProto);
            TryInitializeTransientNotificationProto(protosDb, AtdNotifications.ExcavatorCompletedId, ref s_excavatorCompletedNotificationProto);
            TryInitializeTransientNotificationProto(protosDb, AtdNotifications.DebrisCleanupNoneFoundId, ref s_debrisCleanupNoneFoundNotificationProto);
            TryInitializeTransientNotificationProto(protosDb, AtdNotifications.DebrisCleanupNoneReachableId, ref s_debrisCleanupNoneReachableNotificationProto);
        }

        private static void TryInitializeTransientNotificationProto<TProto>(
            ProtosDb protosDb,
            Proto.ID id,
            ref TProto? proto) where TProto : NotificationProto
        {
            if (protosDb.TryGetProto(id, out TProto resolvedProto))
                proto = resolvedProto;
            else
                Log.Warning("[ATD] Transient notification proto not found: " + id.Value);
        }

        private static void ResetTransientNotifications()
        {
            s_notificationsManager = null;
            s_rampAccessFailedNotificationProto = null;
            s_rampAccessTruncatedNotificationProto = null;
            s_rampAccessNotAccessibleNotificationProto = null;
            s_rampAccessSnapshotTooLargeNotificationProto = null;
            s_farmingCompleteNotificationProto = null;
            s_excavatorCompletedNotificationProto = null;
            s_debrisCleanupNoneFoundNotificationProto = null;
            s_debrisCleanupNoneReachableNotificationProto = null;
            s_transientNotificationsByKey.Clear();
        }

        private static void UpdateTowerRampWarningNotification(IAreaManagingTower tower, RampPlacementOutcome outcome)
        {
            if (!AutoTerrainDesignationsMod.RampNotificationsEnabled)
            {
                ClearTowerRampWarningNotification(tower);
                return;
            }

            if (!TryGetRampWarningNotification(outcome, out TransientNotificationKind kind, out EntityNotificationProto<string>? proto))
            {
                ClearTowerRampWarningNotification(tower);
                return;
            }

            if (HasTransientTowerNotification(tower, kind))
                return;

            ClearTowerRampWarningNotification(tower);
            AddTransientTowerNotification(tower, kind, proto);
        }

        private static void UpdateTowerSnapshotTooLargeWarningNotification(
            IAreaManagingTower tower)
        {
            if (!AutoTerrainDesignationsMod.RampNotificationsEnabled)
            {
                ClearTowerRampWarningNotification(tower);
                return;
            }

            if (HasTransientTowerNotification(
                    tower,
                    TransientNotificationKind.RampAccessSnapshotTooLarge))
                return;

            ClearTransientTowerNotification(
                tower,
                TransientNotificationKind.RampAccessFailed);
            ClearTransientTowerNotification(
                tower,
                TransientNotificationKind.RampAccessTruncated);
            ClearTransientTowerNotification(
                tower,
                TransientNotificationKind.RampAccessNotAccessible);
            AddTransientTowerNotification(
                tower,
                TransientNotificationKind.RampAccessSnapshotTooLarge,
                s_rampAccessSnapshotTooLargeNotificationProto);
        }

        private static bool TryGetRampWarningNotification(
            RampPlacementOutcome outcome,
            out TransientNotificationKind kind,
            out EntityNotificationProto<string>? proto)
        {
            if (outcome == RampPlacementOutcome.Failed)
            {
                kind = TransientNotificationKind.RampAccessFailed;
                proto = s_rampAccessFailedNotificationProto;
                return true;
            }

            if (outcome == RampPlacementOutcome.Truncated)
            {
                kind = TransientNotificationKind.RampAccessTruncated;
                proto = s_rampAccessTruncatedNotificationProto;
                return true;
            }

            if (outcome == RampPlacementOutcome.NotAccessible)
            {
                kind = TransientNotificationKind.RampAccessNotAccessible;
                proto = s_rampAccessNotAccessibleNotificationProto;
                return true;
            }

            kind = default;
            proto = null;
            return false;
        }

        internal static void PurgeTransientNotificationsForSave()
        {
            if (s_notificationsManager == null)
                return;

            foreach (NotificationId notificationId in s_transientNotificationsByKey.Values.ToList())
                s_notificationsManager.RemoveNotification(notificationId);
            s_transientNotificationsByKey.Clear();

            foreach (INotification notification in s_notificationsManager.FetchAllNotifications().ToList())
            {
                if (AtdNotifications.IsAtdProto(notification.Proto))
                    s_notificationsManager.RemoveNotification(notification.NotificationId);
            }
        }

        internal static void RestoreTransientNotificationsAfterSave()
        {
            if (s_entitiesManager == null)
                return;
            foreach (KeyValuePair<EntityId, ATDTowerSettings> kvp in s_towerSettingsByEntityId.ToList())
            {
                if (!kvp.Value.LastRampOutcome.HasValue)
                    continue;
                if (kvp.Value.SuppressLastRampWarningNotification)
                    continue;

                if (s_entitiesManager.TryGetEntity<IEntity>(kvp.Key, out IEntity entity) && entity is IAreaManagingTower tower)
                    UpdateTowerRampWarningNotification(tower, kvp.Value.LastRampOutcome.Value);
            }

        }

        private static void AddTowerDebrisCleanupEmptyNotification(
            IAreaManagingTower tower, bool debrisWasFound)
        {
            if (s_notificationsManager == null)
                return;
            EntityNotificationProto<string>? proto = debrisWasFound
                ? s_debrisCleanupNoneReachableNotificationProto
                : s_debrisCleanupNoneFoundNotificationProto;
            if (proto == null || !(tower is IObjectWithTitle objectWithTitle))
                return;
            s_notificationsManager.AddNotification(
                proto,
                Option<IObjectWithTitle>.Create(objectWithTitle),
                string.Empty);
        }

        private static void ClearTowerRampWarningNotification(IAreaManagingTower tower)
        {
            ClearTransientTowerNotification(tower, TransientNotificationKind.RampAccessFailed);
            ClearTransientTowerNotification(tower, TransientNotificationKind.RampAccessTruncated);
            ClearTransientTowerNotification(tower, TransientNotificationKind.RampAccessNotAccessible);
            ClearTransientTowerNotification(tower, TransientNotificationKind.RampAccessSnapshotTooLarge);
        }

        private static void AddFarmingCompleteNotification(IAreaManagingTower tower)
        {
            if (s_notificationsManager == null || s_farmingCompleteNotificationProto == null)
                return;

            if (!(tower is IObjectWithTitle objectWithTitle))
                return;

            s_notificationsManager.AddNotification(
                s_farmingCompleteNotificationProto,
                Option<IObjectWithTitle>.Create(objectWithTitle),
                string.Empty);
        }

        private static void AddExcavatorCompletedNotification(IObjectWithTitle objectWithTitle, string vehicleTypeName)
        {
            if (s_notificationsManager == null || s_excavatorCompletedNotificationProto == null)
                return;

            s_notificationsManager.AddNotification(
                s_excavatorCompletedNotificationProto,
                Option<IObjectWithTitle>.Create(objectWithTitle),
                vehicleTypeName);
        }

        private static bool HasTransientTowerNotification(IAreaManagingTower tower, TransientNotificationKind kind)
        {
            if (!TryGetTowerEntityId(tower, out EntityId entityId))
                return false;

            TransientNotificationKey key = new TransientNotificationKey(entityId, kind);
            return s_transientNotificationsByKey.ContainsKey(key);
        }

        private static void AddTransientTowerNotification(
            IAreaManagingTower tower,
            TransientNotificationKind kind,
            EntityNotificationProto<string>? proto)
        {
            if (s_notificationsManager == null || proto == null)
                return;

            if (!TryGetTowerEntityId(tower, out EntityId entityId))
                return;

            TransientNotificationKey key = new TransientNotificationKey(entityId, kind);
            if (s_transientNotificationsByKey.ContainsKey(key))
                return;

            if (!(tower is IObjectWithTitle objectWithTitle))
                return;

            NotificationId notificationId = s_notificationsManager.AddNotification(
                proto,
                Option<IObjectWithTitle>.Create(objectWithTitle),
                string.Empty);
            s_transientNotificationsByKey[key] = notificationId;
        }

        private static void ClearTransientTowerNotification(IAreaManagingTower tower, TransientNotificationKind kind)
        {
            if (s_notificationsManager == null)
                return;

            if (!TryGetTowerEntityId(tower, out EntityId entityId))
                return;

            TransientNotificationKey key = new TransientNotificationKey(entityId, kind);
            if (s_transientNotificationsByKey.TryGetValue(key, out NotificationId notificationId))
            {
                s_notificationsManager.RemoveNotification(notificationId);
                s_transientNotificationsByKey.Remove(key);
            }
        }
    }
}
