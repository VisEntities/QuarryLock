This plugin enables players to deploy code locks onto quarries and pump jacks in the same way code locks are deployed on doors and containers (no commands required).

[Demonstration](https://www.youtube.com/watch?v=9xzvF_8921M)

-------------

## Features
* Players can deploy up to 3 code locks on different parts of the quarry or pump jack, including the engine switch, fuel storage, and resources storage.
* Automatically grants clan and team members access to use the code lock once deployed.
* Code locks can be deployed either unlocked (similar to vanilla behavior) or set to lock automatically with a randomly generated code.
* Compatible with other code lock plugins such as [AutoCode](https://umod.org/plugins/auto-code).
* Works with both deployable and static quarries.

------------------

## Permissions

* `quarrylock.admin` - Allows bypassing locked quarries and pump jacks owned by other players, enabling the toggling of the engine or the looting of storage containers.

----------------

## Configuration

```json
{
  "Version": "2.0.0",
  "Enable Auto Locking On Placement": false,
  "Auto Authorize Team": true,
  "Auto Authorize Clan": false
}
```

-----------------------

## Localization

```json
{
  "Locked": "It's locked...",
  "CodeLockDeployed": "Code lock deployed successfully.",
  "AutoLocked": "Auto locked with code: <color=#FABE28>{0}</color>.",
  "TeamAuthorized": "Your team members have been automatically whitelisted on this code lock.",
  "ClanAuthorized": "Your clan members have been automatically whitelisted on this code lock."
}
```

------------------

## Developer Hooks
### OnItemDeployed
This is an Oxide hook that triggers when a deployable item is placed on another entity. For compatibility with other plugins that utilize this hook, this plugin invokes it whenever a code lock is deployed on a quarry or pump jack.

To determine which part of the quarry or pump jack the code lock was deployed on, get the parent entity of the code lock and then compare it with either the type or the prefab name of the quarry part.

```cs
 void OnItemDeployed(Deployer deployerItem, MiningQuarry miningQuarry, CodeLock codeLock)

```

------------------


## Screenshots

![](https://i.ibb.co/mhKgHD0/Group-1069.png)

----------------------

## Credits
 * Rewritten from scratch and maintained to present by **VisEntities**
 * Originally created by **Orange**, up to version 1.2.1