using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/*
 * Rewritten from scratch and maintained to present by VisEntities
 * Originally created by Orange, up to version 1.2.1
 */

namespace Oxide.Plugins
{
    [Info("Quarry Lock", "VisEntities", "2.0.0")]
    [Description("Deploy code locks onto quarries and pump jacks.")]
    public class QuarryLock : RustPlugin
    {
        #region 3rd Party Dependencies

        [PluginReference]
        private readonly Plugin Clans;

        #endregion 3rd Party Dependencies

        #region Fields

        private static QuarryLock _plugin;
        private static Configuration _config;

        private const int ITEM_ID_CODE_LOCK = 1159991980;

        private const string PREFAB_CODE_LOCK = "assets/prefabs/locks/keypad/lock.code.prefab";

        private const string PREFAB_QUARRY = "assets/prefabs/deployable/quarry/mining_quarry.prefab";
        private const string PREFAB_QUARRY_ENGINE = "assets/prefabs/deployable/quarry/engineswitch.prefab";
        private const string PREFAB_QUARRY_FUEL_STORAGE = "assets/prefabs/deployable/quarry/fuelstorage.prefab";
        private const string PREFAB_QUARRY_HOPPER_OUTPUT = "assets/prefabs/deployable/quarry/hopperoutput.prefab";

        private const string PREFAB_PUMP_JACK = "assets/prefabs/deployable/oil jack/mining.pumpjack.prefab";
        private const string PREFAB_PUMP_JACK_ENGINE = "assets/prefabs/deployable/oil jack/engineswitch.prefab";
        private const string PREFAB_PUMP_JACK_FUEL_STORAGE = "assets/prefabs/deployable/oil jack/fuelstorage.prefab";
        private const string PREFAB_PUMP_JACK_CRUDE_OUTPUT = "assets/prefabs/deployable/oil jack/crudeoutput.prefab";

        private const string FX_CODE_LOCK_DEPLOY = "assets/prefabs/locks/keypad/effects/lock-code-deploy.prefab";
        
        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Enable Auto Locking On Placement")]
            public bool EnableAutoLockingOnPlacement { get; set; }

            [JsonProperty("Auto Authorize Team")]
            public bool AutoAuthorizeTeam { get; set; }

            [JsonProperty("Auto Authorize Clan")]
            public bool AutoAuthorizeClan { get; set; }
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

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                EnableAutoLockingOnPlacement = false,
                AutoAuthorizeTeam = true,
                AutoAuthorizeClan = false
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
            _config = null;
            _plugin = null;
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
                Vector3 localPosition;
                Quaternion localRotation;

                switch (storageContainer.PrefabName)
                {
                    case PREFAB_QUARRY_FUEL_STORAGE:
                        {
                            localPosition = new Vector3(0.45f, 0.65f, 0.50f);
                            localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                            break;
                        }
                    case PREFAB_QUARRY_HOPPER_OUTPUT:
                        {
                            localPosition = new Vector3(-0.03f, 1.9f, 1.3f);
                            localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                            break;
                        }
                    case PREFAB_PUMP_JACK_FUEL_STORAGE:
                        {
                            localPosition = new Vector3(-0.70f, 0.56f, 0.49f);
                            localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                            break;
                        }
                    case PREFAB_PUMP_JACK_CRUDE_OUTPUT:
                        {
                            localPosition = new Vector3(0.29f, 0.60f, 0.001f);
                            localRotation = Quaternion.Euler(0.0f, 0.0f, 0.0f);
                            break;
                        }
                    default:
                        return null;
                }

                CodeLock codeLock = DeployCodeLock(player, storageContainer, localPosition, localRotation);
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
                    {
                        existingCodeLock.whitelistPlayers.Add(player.userID);
                    }

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

            EngineSwitch engineSwitch = miningQuarry.GetComponentInChildren<EngineSwitch>();
            if (engineSwitch == null)
                return null;

            CodeLock existingCodeLock = engineSwitch.GetSlot(BaseEntity.Slot.Lock) as CodeLock;
            Item item = player.GetActiveItem();

            if (existingCodeLock == null && item != null && item.info.itemid == ITEM_ID_CODE_LOCK)
            {
                Vector3 localPosition;
                Quaternion localRotation;

                switch (engineSwitch.PrefabName)
                {
                    case PREFAB_QUARRY_ENGINE:
                        {
                            localPosition = new Vector3(0.07f, 0.91f, -0.70f);
                            localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                            break;
                        }
                    case PREFAB_PUMP_JACK_ENGINE:
                        {
                            localPosition = new Vector3(0.38f, 0.87f, -0.68f);
                            localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);
                            break;
                        }
                    default:
                        return null;
                }

                CodeLock codeLock = DeployCodeLock(player, engineSwitch, localPosition, localRotation);
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

        #region Deploying Code Lock

        private CodeLock DeployCodeLock(BasePlayer player, BaseEntity parent, Vector3 localPosition, Quaternion localRotation)
        {
            Vector3 worldPosition = TransformUtil.LocalToWorldPosition(parent.transform, localPosition);
            Quaternion worldRotation = TransformUtil.LocalToWorldRotation(parent.transform, localRotation);

            CodeLock codeLock = GameManager.server.CreateEntity(PREFAB_CODE_LOCK, worldPosition, worldRotation) as CodeLock;
            if (codeLock == null)
                return null;

            codeLock.OwnerID = player.userID;

            codeLock.SetParent(parent, true, true);
            codeLock.Spawn();

            parent.SetSlot(BaseEntity.Slot.Lock, codeLock);

            if (_config.EnableAutoLockingOnPlacement)
            {
                string randomCode = GenerateRandomCode();
                codeLock.code = randomCode;
                codeLock.whitelistPlayers.Add(player.userID);
                codeLock.SetFlag(BaseEntity.Flags.Locked, true);

                SendReplyToPlayer(player, Lang.AutoLocked, randomCode);

                if (_config.AutoAuthorizeClan)
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
                                {
                                    if (memberId != player.userID)
                                    {
                                        codeLock.whitelistPlayers.Add(memberId);
                                    }
                                }

                                SendReplyToPlayer(player, Lang.ClanAuthorized);
                            }
                        }
                    }
                }
                if (_config.AutoAuthorizeTeam)
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
  
        #endregion Deploying Code Lock

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

        #region Helper Classes

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

        private static class TransformUtil
        {
            public static void WorldToLocal(Transform parentTransform, Transform childTransform, out Vector3 localPosition, out Quaternion localRotation)
            {
                localPosition = parentTransform.InverseTransformPoint(childTransform.position);
                localRotation = Quaternion.Inverse(parentTransform.rotation) * childTransform.rotation;
            }

            public static void LocalToWorld(Transform parentTransform, Transform childTransform, out Vector3 worldPosition, out Quaternion worldRotation)
            {
                worldPosition = parentTransform.TransformPoint(childTransform.localPosition);
                worldRotation = parentTransform.rotation * childTransform.localRotation;
            }

            public static Quaternion WorldToLocalRotation(Transform parentTransform, Quaternion worldRotation)
            {
                return Quaternion.Inverse(parentTransform.rotation) * worldRotation;
            }

            public static Vector3 WorldToLocalPosition(Transform parentTransform, Vector3 worldPosition)
            {
                return parentTransform.InverseTransformPoint(worldPosition);
            }

            public static Quaternion LocalToWorldRotation(Transform parentTransform, Quaternion localRotation)
            {
                return parentTransform.rotation * localRotation;
            }

            public static Vector3 LocalToWorldPosition(Transform parentTransform, Vector3 localPosition)
            {
                return parentTransform.TransformPoint(localPosition);
            }
        }

        #endregion Helper Classes

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
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.Locked] = "It's locked...",
                [Lang.CodeLockDeployed] = "Code lock deployed successfully.",
                [Lang.AutoLocked] = "Auto locked with code: <color=#FABE28>{0}</color>.",
                [Lang.TeamAuthorized] = "Your team members have been automatically whitelisted on this code lock.",
                [Lang.ClanAuthorized] = "Your clan members have been automatically whitelisted on this code lock."
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