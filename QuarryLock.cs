using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

/*
 * Rewritten from scratch and maintained to present by VisEntities
 * Originally created by Orange, up to version 1.2.1
 */

namespace Oxide.Plugins
{
    [Info("Quarry Lock", "VisEntities", "2.3.1")]
    [Description("Deploy code locks onto quarries and pump jacks.")]
    public class QuarryLock : RustPlugin
    {
        #region 3rd Party Dependencies

        [PluginReference]
        private readonly Plugin Clans, Friends;

        #endregion 3rd Party Dependencies

        #region Fields

        private static QuarryLock _plugin;
        private static Configuration _config;
        private Coroutine _codeLockParentUpdateCoroutine;

        private const int ITEM_ID_CODE_LOCK = 1159991980;

        private const string PREFAB_CODE_LOCK = "assets/prefabs/locks/keypad/lock.code.prefab";
        private const string PREFAB_QUARRY_ENGINE = "assets/prefabs/deployable/quarry/engineswitch.prefab";
        private const string PREFAB_QUARRY_FUEL = "assets/prefabs/deployable/quarry/fuelstorage.prefab";
        private const string PREFAB_QUARRY_HOPPER = "assets/prefabs/deployable/quarry/hopperoutput.prefab";
        private const string PREFAB_PUMP_JACK_ENGINE = "assets/prefabs/deployable/oil jack/engineswitch.prefab";
        private const string PREFAB_PUMP_JACK_FUEL = "assets/prefabs/deployable/oil jack/fuelstorage.prefab";
        private const string PREFAB_PUMP_JACK_HOPPER = "assets/prefabs/deployable/oil jack/crudeoutput.prefab";

        private const string FX_CODE_LOCK_DEPLOY = "assets/prefabs/locks/keypad/effects/lock-code-deploy.prefab";
        
        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }
            
            [JsonProperty("Only Quarry Owner Can Place Locks")]
            public bool OnlyQuarryOwnerCanPlaceLocks { get; set; }

            [JsonProperty("Enable Auto Locking On Placement")]
            public bool EnableAutoLockingOnPlacement { get; set; }

            [JsonProperty("Enable Lock Placement On Static Extractors")]
            public bool EnableLockPlacementOnStaticExtractors { get; set; }

            [JsonProperty("Auto Authorize Teammates")]
            public bool AutoAuthorizeTeammates { get; set; }

            [JsonProperty("Auto Authorize Clanmates")]
            public bool AutoAuthorizeClanmates { get; set; }

            [JsonProperty("Auto Authorize Friends")]
            public bool AutoAuthorizeFriends { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            if (string.Compare(_config.Version, "2.1.0") < 0)
            {
                _config.EnableLockPlacementOnStaticExtractors = defaultConfig.EnableLockPlacementOnStaticExtractors;
            }

            if (string.Compare(_config.Version, "2.2.0") < 0)
            {
                _config.AutoAuthorizeFriends = defaultConfig.AutoAuthorizeFriends;
            }

            if (string.Compare(_config.Version, "2.3.0") < 0)
            {
                _config.OnlyQuarryOwnerCanPlaceLocks = defaultConfig.OnlyQuarryOwnerCanPlaceLocks;
            }

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                OnlyQuarryOwnerCanPlaceLocks = true,
                EnableAutoLockingOnPlacement = false,
                EnableLockPlacementOnStaticExtractors = false,
                AutoAuthorizeTeammates = true,
                AutoAuthorizeFriends = false,
                AutoAuthorizeClanmates = false,
            };
        }

        #endregion Configuration

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            PermissionUtil.RegisterPermissions();
        }

        private void Unload()
        {
            CoroutineUtil.StopAllCoroutines();
            _config = null;
            _plugin = null;
        }

        private void OnServerInitialized(bool isStartup)
        {
            if (!isStartup)
                return;

            CoroutineUtil.StartCoroutine(Guid.NewGuid().ToString(), UpdateAllCodeLockParentsCoroutine());      
        }

        private object CanLootEntity(BasePlayer player, ResourceExtractorFuelStorage storageContainer)
        {
            if (player == null || storageContainer == null)
                return null;

            MiningQuarry miningQuarry = storageContainer.GetParentEntity() as MiningQuarry;
            if (miningQuarry == null)
                return null;
           
            CodeLock existingCodeLock = storageContainer.GetSlot(BaseEntity.Slot.Lock) as CodeLock;
            Item item = player.GetActiveItem();

            if (existingCodeLock == null && item != null && item.info.itemid == ITEM_ID_CODE_LOCK)
            {
                if (miningQuarry.isStatic && !_config.EnableLockPlacementOnStaticExtractors)
                {
                    SendReplyToPlayer(player, Lang.StaticExtractorLockingBlocked);
                    return true;
                }

                if (miningQuarry.OwnerID != 0 && _config.OnlyQuarryOwnerCanPlaceLocks && player.userID != miningQuarry.OwnerID)
                {
                    SendReplyToPlayer(player, Lang.OnlyOwnerCanPlaceLocks);
                    return true;
                }

                Vector3 localPosition;
                Quaternion localRotation;

                switch (storageContainer.PrefabName)
                {
                    case PREFAB_QUARRY_FUEL:
                        {
                            localPosition = new Vector3(0.45f, 0.65f, 0.50f);
                            localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                            break;
                        }
                    case PREFAB_QUARRY_HOPPER:
                        {
                            localPosition = new Vector3(-0.03f, 1.9f, 1.3f);
                            localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                            break;
                        }
                    case PREFAB_PUMP_JACK_FUEL:
                        {
                            localPosition = new Vector3(-0.70f, 0.56f, 0.49f);
                            localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                            break;
                        }
                    case PREFAB_PUMP_JACK_HOPPER:
                        {
                            localPosition = new Vector3(0.29f, 0.60f, 0.001f);
                            localRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f);
                            break;
                        }
                    default:
                        return null;
                }

                CodeLock codeLock = DeployCodeLock(player, item, storageContainer, localPosition, localRotation);
                if (codeLock != null)
                {
                    item.UseItem(1);
                    // For compatibility with plugins that utilize the 'OnItemDeployed' hook.
                    Interface.CallHook("OnItemDeployed", item.GetHeldEntity(), miningQuarry, codeLock);
                    return true;
                }
            }
            else if (existingCodeLock != null)
            {
                if (PermissionUtil.VerifyHasPermission(player, PermissionUtil.ADMIN))
                {
                    if (!existingCodeLock.whitelistPlayers.Contains(player.userID))
                        existingCodeLock.whitelistPlayers.Add(player.userID);

                    return null;
                }
                else if (!existingCodeLock.OnTryToOpen(player))
                {
                    SendReplyToPlayer(player, Lang.Locked);
                    return true;
                }
            }

            return null;
        }

        private object OnQuarryToggle(MiningQuarry miningQuarry, BasePlayer player)
        {
            if (miningQuarry == null || player == null)
                return null;

            EngineSwitch engineSwitch = miningQuarry.engineSwitchPrefab.instance as EngineSwitch;
            if (engineSwitch == null)
                return null;

            CodeLock existingCodeLock = engineSwitch.GetSlot(BaseEntity.Slot.Lock) as CodeLock;
            Item item = player.GetActiveItem();

            if (existingCodeLock == null && item != null && item.info.itemid == ITEM_ID_CODE_LOCK)
            {
                if (miningQuarry.isStatic && !_config.EnableLockPlacementOnStaticExtractors)
                {
                    SendReplyToPlayer(player, Lang.StaticExtractorLockingBlocked);
                    return true;
                }

                if (miningQuarry.OwnerID != 0 && _config.OnlyQuarryOwnerCanPlaceLocks && player.userID != miningQuarry.OwnerID)
                {
                    SendReplyToPlayer(player, Lang.OnlyOwnerCanPlaceLocks);
                    return true;
                }

                Vector3 localPosition;
                Quaternion localRotation;

                if (engineSwitch.PrefabName == PREFAB_QUARRY_ENGINE)
                {
                    if (miningQuarry.isStatic)
                    {
                        localPosition = new Vector3(0.29f, 0.82f, 0.07f);
                        localRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f);
                    }
                    else
                    {
                        localPosition = new Vector3(0.07f, 0.91f, -0.70f);
                        localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                    }
                }
                else if (engineSwitch.PrefabName == PREFAB_PUMP_JACK_ENGINE)
                {
                    if (miningQuarry.isStatic)
                    {
                        localPosition = new Vector3(0.06f, 0.36f, -0.28f);
                        localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                    }
                    else
                    {
                        localPosition = new Vector3(0.38f, 0.87f, -0.68f);
                        localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                    }
                }
                else
                    return null;

                CodeLock codeLock = DeployCodeLock(player, item, engineSwitch, localPosition, localRotation);
                if (codeLock != null)
                {
                    item.UseItem(1);
                    // For compatibility with plugins that utilize the 'OnItemDeployed' hook.
                    Interface.CallHook("OnItemDeployed", item.GetHeldEntity(), miningQuarry, codeLock);
                    return true;
                }
            }
            else if (existingCodeLock != null)
            {
                if (!existingCodeLock.OnTryToOpen(player) && !PermissionUtil.VerifyHasPermission(player, PermissionUtil.ADMIN))
                {
                    SendReplyToPlayer(player, Lang.Locked);
                    return true;
                }
            }

            return null;
        }

        #endregion Oxide Hooks

        #region Code Lock Deployment

        private CodeLock DeployCodeLock(BasePlayer player, Item deployerItem, BaseEntity parent, Vector3 localPosition, Quaternion localRotation)
        {
            Vector3 worldPosition = parent.transform.TransformPoint(localPosition);
            Quaternion worldRotation = parent.transform.rotation * localRotation;

            CodeLock codeLock = GameManager.server.CreateEntity(PREFAB_CODE_LOCK, worldPosition, worldRotation) as CodeLock;
            if (codeLock == null)
                return null;

            codeLock.OwnerID = player.userID;

            codeLock.SetParent(parent, parent.GetSlotAnchorName(BaseEntity.Slot.Lock), true, true);
            codeLock.OnDeployed(parent, player, deployerItem);
            codeLock.Spawn();

            parent.SetSlot(BaseEntity.Slot.Lock, codeLock);
            // This's necessary to prevent hopper and fuel storage from being destroyed and recreated on server restart.
            parent.EnableSaving(true);

            if (_config.EnableAutoLockingOnPlacement)
            {
                string randomCode = GenerateRandomCode();
                codeLock.code = randomCode;
                codeLock.whitelistPlayers.Add(player.userID);
                codeLock.SetFlag(BaseEntity.Flags.Locked, true);

                SendReplyToPlayer(player, Lang.AutoLocked, randomCode);

                if (_config.AutoAuthorizeClanmates)
                {
                    string clanTag = ClanUtil.GetClanTagOfPlayer(player);
                    if (!string.IsNullOrEmpty(clanTag))
                    {
                        JObject clanInfo = ClanUtil.GetClanInfo(clanTag);
                        if (clanInfo != null)
                        {
                            JArray members = (JArray)clanInfo["members"];
                            if (members.Count > 0)
                            {
                                foreach (ulong memberId in members)
                                    codeLock.whitelistPlayers.Add(memberId);        

                                SendReplyToPlayer(player, Lang.ClanAuthorized);
                            }
                        }
                    }
                }
                if (_config.AutoAuthorizeFriends)
                {
                    ulong[] friendsList = FriendsUtil.GetFriendsOfPlayer((ulong)player.userID);
                    if (friendsList.Length > 0)
                    {
                        foreach (ulong friendId in friendsList)
                            codeLock.whitelistPlayers.Add(friendId);
                 
                        SendReplyToPlayer(player, Lang.FriendsAuthorized);
                    }
                }
                if (_config.AutoAuthorizeTeammates)
                {
                    if (player.Team != null && player.Team.members.Count > 1)
                    {
                        foreach (ulong memberId in player.Team.members)
                        {
                            if (memberId != player.userID)
                            {
                                codeLock.whitelistPlayers.Add(memberId);
                            }
                        }

                        SendReplyToPlayer(player, Lang.TeamAuthorized);
                    }
                }
            }

            SendReplyToPlayer(player, Lang.CodeLockDeployed);
            RunEffect(FX_CODE_LOCK_DEPLOY, codeLock.transform.position);
            return codeLock;
        }

        private string GenerateRandomCode()
        {
            return Random.Range(1000, 9999).ToString();
        }

        #endregion Code Lock Deployment

        #region Code Lock Parent Refresh

        /// <summary>
        /// Updates the parent of code locks attached to the mining quarry children (engine, hopper, and fuel storage)
        /// to the new instances created after server restart. This's necessary because these children are destroyed
        /// and recreated when the server restarts.
        /// </summary>
        private IEnumerator UpdateAllCodeLockParentsCoroutine()
        {
            foreach (CodeLock codeLock in BaseNetworkable.serverEntities.OfType<CodeLock>())
            {
                yield return CoroutineEx.waitForSeconds(0.1f);

                if (codeLock == null)
                    continue;

                BaseEntity parent = codeLock.GetParentEntity();
                if (parent == null)
                    continue;

                MiningQuarry miningQuarry = parent.GetParentEntity() as MiningQuarry;
                if (miningQuarry == null)
                    continue;

                BaseEntity newParent = null;
                Vector3 localPosition = Vector3.zero;
                Quaternion localRotation = Quaternion.identity;

                switch (parent.PrefabName)
                {
                    case PREFAB_QUARRY_FUEL:
                        {
                            localPosition = new Vector3(0.45f, 0.65f, 0.50f);
                            localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                            newParent = miningQuarry.fuelStoragePrefab.instance;
                            break;
                        }
                    case PREFAB_QUARRY_HOPPER:
                        {
                            localPosition = new Vector3(-0.03f, 1.9f, 1.3f);
                            localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                            newParent = miningQuarry.hopperPrefab.instance;
                            break;
                        }
                    case PREFAB_PUMP_JACK_FUEL:
                        {
                            localPosition = new Vector3(-0.70f, 0.56f, 0.49f);
                            localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                            newParent = miningQuarry.fuelStoragePrefab.instance;
                            break;
                        }
                    case PREFAB_PUMP_JACK_HOPPER:
                        {
                            localPosition = new Vector3(0.29f, 0.60f, 0.001f);
                            localRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f);
                            newParent = miningQuarry.hopperPrefab.instance;
                            break;
                        }
                    case PREFAB_QUARRY_ENGINE:
                        {
                            if (miningQuarry.isStatic)
                            {
                                localPosition = new Vector3(0.29f, 0.82f, 0.07f);
                                localRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f);
                                newParent = miningQuarry.engineSwitchPrefab.instance;
                            }
                            else
                            {
                                localPosition = new Vector3(0.07f, 0.91f, -0.70f);
                                localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                                newParent = miningQuarry.engineSwitchPrefab.instance;
                            }
                            break;
                        }
                    case PREFAB_PUMP_JACK_ENGINE:
                        {
                            if (miningQuarry.isStatic)
                            {
                                localPosition = new Vector3(0.06f, 0.36f, -0.28f);
                                localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                                newParent = miningQuarry.engineSwitchPrefab.instance;
                            }
                            else
                            {
                                localPosition = new Vector3(0.38f, 0.87f, -0.68f);
                                localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                                newParent = miningQuarry.engineSwitchPrefab.instance;
                            }
                            break;
                        }
                }

                if (newParent == null)
                    continue;

                codeLock.SetParent(null);
                codeLock.gameObject.Identity();

                codeLock.SetParent(newParent, true, true);
                codeLock.transform.localPosition = localPosition;
                codeLock.transform.localRotation = localRotation;

                newParent.SetSlot(BaseEntity.Slot.Lock, codeLock);
                newParent.EnableSaving(true);

                parent.Kill();
            }
        }

        #endregion Code Lock Parent Refresh

        #region Coroutine Util

        private static class CoroutineUtil
        {
            private static readonly Dictionary<string, Coroutine> _activeCoroutines = new Dictionary<string, Coroutine>();

            public static void StartCoroutine(string coroutineName, IEnumerator coroutineFunction)
            {
                StopCoroutine(coroutineName);

                Coroutine coroutine = ServerMgr.Instance.StartCoroutine(coroutineFunction);
                _activeCoroutines[coroutineName] = coroutine;
            }

            public static void StopCoroutine(string coroutineName)
            {
                if (_activeCoroutines.TryGetValue(coroutineName, out Coroutine coroutine))
                {
                    if (coroutine != null)
                        ServerMgr.Instance.StopCoroutine(coroutine);

                    _activeCoroutines.Remove(coroutineName);
                }
            }

            public static void StopAllCoroutines()
            {
                foreach (string coroutineName in _activeCoroutines.Keys.ToArray())
                {
                    StopCoroutine(coroutineName);
                }
            }
        }

        #endregion Coroutine Util

        #region Clan Integration

        private static class ClanUtil
        {
            public static string GetClanTagOfPlayer(BasePlayer player)
            {
                if (!VerifyPluginBeingLoaded(_plugin.Clans))
                    return "";

                string clanTag = (string)_plugin.Clans.Call("GetClanOf", player);
                if (clanTag == null)
                    return "";

                return clanTag;
            }

            public static JObject GetClanInfo(string clanTag)
            {
                if (!VerifyPluginBeingLoaded(_plugin.Clans))
                    return null;

                JObject clan = (JObject)_plugin.Clans.Call("GetClan", clanTag);
                if (clan == null)
                    return null;

                return clan;
            }
        }

        #endregion Clan Integration

        #region Friends Integration

        private static class FriendsUtil
        {            
            public static ulong[] GetFriendsOfPlayer(ulong playerId)
            {
                if (!VerifyPluginBeingLoaded(_plugin.Friends))
                    return new ulong[0];

                return (ulong[])_plugin.Friends.Call("GetFriendList", playerId);
            }
        }

        #endregion Friends Integration

        #region Permission

        private static class PermissionUtil
        {
            public const string ADMIN = "quarrylock.admin";

            public static void RegisterPermissions()
            {
                _plugin.permission.RegisterPermission(ADMIN, _plugin);
            }

            public static bool VerifyHasPermission(BasePlayer player, string permissionName = ADMIN)
            {
                return _plugin.permission.UserHasPermission(player.UserIDString, permissionName);
            }
        }

        #endregion Permission

        #region Helper Functions

        private static void RunEffect(string prefab, Vector3 worldPosition = default(Vector3), Vector3 worldDirection = default(Vector3), Connection effectRecipient = null, bool sendToAll = false)
        {
            Effect.server.Run(prefab, worldPosition, worldDirection, effectRecipient, sendToAll);
        }

        private static bool VerifyPluginBeingLoaded(Plugin plugin)
        {
            if (plugin != null && plugin.IsLoaded)
                return true;
            else
                return false;
        }

        #endregion Helper Functions

        #region Localization

        private class Lang
        {
            public const string Locked = "Locked";
            public const string CodeLockDeployed = "CodeLockDeployed";
            public const string AutoLocked = "AutoLocked";
            public const string TeamAuthorized = "TeamAuthorized";
            public const string ClanAuthorized = "ClanAuthorized";
            public const string FriendsAuthorized = "FriendsAuthorized";
            public const string StaticExtractorLockingBlocked = "StaticExtractorLockingBlocked";
            public const string OnlyOwnerCanPlaceLocks = "OnlyOwnerCanPlaceLocks";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.Locked] = "It's locked...",
                [Lang.CodeLockDeployed] = "Code lock deployed successfully.",
                [Lang.AutoLocked] = "Auto locked with code: <color=#FABE28>{0}</color>.",
                [Lang.TeamAuthorized] = "Your team members have been automatically whitelisted on this code lock.",
                [Lang.ClanAuthorized] = "Your clan members have been automatically whitelisted on this code lock.",
                [Lang.FriendsAuthorized] = "Your friends have been automatically whitelisted on this code lock.",
                [Lang.StaticExtractorLockingBlocked] = "Cannot place code locks on static resource extractors.",
                [Lang.OnlyOwnerCanPlaceLocks] = "Only the quarry's owner can place locks on it."
            }, this, "en");
        }

        private void SendReplyToPlayer(BasePlayer player, string messageKey, params object[] args)
        {
            string message = lang.GetMessage(messageKey, this, player.UserIDString);
            if (args.Length > 0)
                message = string.Format(message, args);

            SendReply(player, message);
        }

        #endregion Localization
    }
}