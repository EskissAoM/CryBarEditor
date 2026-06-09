# ProtoUnit Attributes and Flags

This document describes the most common `ProtoUnit` attributes used in `proto.xml`.

## ProtoUnits (`proto.xml`)

### Attributes

- **Icon**: WPF path for unit icon, relative to `game\ui_myth`.
- **MinimapIcon**: Path to minimap icon texture, relative to the Art folder (`game\art`).
- **MinimapColor**: ProtoUnit minimap colour. Takes RGB colour parameters, which expect floating point values in the `[0.0, 1.0]` interval.

```xml
<MinimapColor red="1.0000" blue="1.0000" green="1.0000"></MinimapColor>
```

- **MinimapSize**: Size of minimap indicator. Defaults to `2.0` if not set.
- **AnimFile**: Path to protoUnit animfile, relative to the Art folder (`game\art`). Can take a culture parameter for setting culture-specific animfiles.
- **SoundSetFile**: Path to protoUnit soundset file, relative to the Sound folder (`game\sound`).
- **PlacementFile**: Path to protoUnit placementRules file, relative to `game\data\gameplay\placement_rules`.
- **DisplayNameID**: String ID for protoUnit displayed name.
- **EditorNameID**: String ID for protoUnit displayed name in Scenario Editor listing.
- **RolloverTextID**: String ID for protoUnit long rollover.
- **ShortRolloverTextID**: String ID for protoUnit short rollover.
- **WorldToolTipTextID**: Deprecated.
- **GoodAgainstStringID**: String ID for protoUnit “Good Against” text to be displayed in long rollover.
- **BadAgainstStringID**: String ID for protoUnit “Bad Against” text to be displayed in long rollover.
- **MaxHitpoints**: ProtoUnit maximum amount of hitpoints.
- **InitialHitpoints**: ProtoUnit initial amount of hitpoints.
- **MaxShieldPoints**: ProtoUnit maximum amount of shield points.
- **InitialShieldPoints**: ProtoUnit initial amount of shield points.
- **UnitRegen**: Defines individual protoUnit regeneration rate. Supported parameters:
  - `idleTimeout`: Minimum amount of time in seconds a unit has to be idle before regeneration begins.
  - `damageTimeout`: Minimum amount of time in seconds since the last time the unit received any damage before regeneration begins.
  - `combatMultiplier`: Multiplies the regeneration by the defined value while the unit is in combat.
  - `rateLimit`: Minimum unit hitpoint ratio that can be reached through degeneration, when setting regeneration rate to a negative value. If regeneration is set to a positive value, the unit will regenerate up to `1 - hitpoint ratio` (for example, if the value is `0.3` then the unit will regenerate up to 70% of its hitpoints).
- **UnitShieldRegen**: Defines protoUnit regeneration rate for shield points. Takes the same parameters as `UnitRegen`.
- **LOS**: ProtoUnit Line of Sight.
- **SharedSelectionUnitTypes**: List of unit types which will share double-selection with this unit.

```xml
<SharedSelectionUnitTypes>
  <UnitType>KuafuHero</UnitType>
</SharedSelectionUnitTypes>
```

- **MaxVelocity**: ProtoUnit base speed value.
- **MaxRunVelocity**: Used in calculations that define when Run animations should be used by the unit, when applicable. If set to a value greater than `MaxVelocity` and the unit has proper Run animations set within its animfile, run animation will be used once the unit’s current speed exceeds the average between `MaxVelocity` and `MaxRunVelocity`.
- **InitialShading**: Sets the initial shading factor to be applied to the unit. Parameters:
  - `type`: Shading type to be applied. Can be set to `bronze`, `stone`, `frost`, `burning`, or `gold`.
  - `factor`: Factor to be used for the provided shading type.
- **DamageShading**: Sets up data for progressive shading tied to unit damage. Parameters:
  - `type`: Shading type to be applied. Can be set to `bronze`, `stone`, `frost`, `burning`, or `gold`.
  - `threshold`: Hitpoint threshold below which shading will be progressively applied.
  - `rate`: Value by which the factor for the provided shading type will be increased according to a defined time interval.
  - `time`: Time interval in milliseconds in which the factor for the provided shading type will be increased by the value given through the `rate` attribute, until reaching `1.0` shading factor.
- **MovementType**: Defines which terrains the unit can move through.
- **TurnRate**: ProtoUnit turning/rotation rate.
- **HeightHitpointBarOffset**: Offset for default hitpoint bar position.
- **UnitAIType**: ProtoUnit UnitAI type. Used for auto-attack behaviour, target selection, and other features related to overall unit AI handling.
- **InitialUnitAIStance**: Defines the initial stance for the UnitAI (`Aggressive`, `Defensive`, `Passive`, or `StandGround`).
- **FormationOrder**: Order in which unit is to be placed on formations. Cannot be set to a value greater than `5`.
- **PopulationCount**: ProtoUnit population count.
- **TrainPoints**: Total amount of time in seconds required to train protoUnit.
- **Cost**: ProtoUnit cost. Takes one parameter, `resourcetype`, which sets the resource type for each entry.
- **CostEscalation**: Multiplicative factor to be applied over protoUnit’s cost for every instance of the same protoUnit queued or in the map.
- **InitialResource**: Initial amount of resources carried by the unit. Takes one parameter, `resourceType`, which sets the resource type.

```xml
<InitialResource resourceType="Food">50.0000</InitialResource>
```

- **CarryCapacity**: Maximum amount of resources that can be carried by the unit. Takes one parameter, `resourceType`, which sets the resource type. For units that don’t carry resources, sets the resource types it’s allowed to gather from when performing a Hunting action. Can have an optional parameter `dropOffMultiplier` that increases the quantity of resources dropped off at this unit.

```xml
<CarryCapacity resourceType="Food" dropoffmultiplier="1.15">500.0000</CarryCapacity>
```

- **PriorityBonusFactor**: Bonus factor to be added to the resource priority factor of a potential gathering target, when querying for new resource deposits after constructing or being tasked to a dropsite or depleting a resource deposit. Takes one parameter, `resourceType`, which sets the resource type.

```xml
<PriorityBonusFactor resourceType="Wood">30.0000</PriorityBonusFactor>
```

- **KillReward**: Resources to be granted when killing this unit to the player who dealt the killing blow. Split between all enemies of the owner when deleting the unit. Takes one parameter, `resourceType`, which sets the resource type.

```xml
<KillReward resourceType="Gold">300.0</KillReward>
```

- **ResourceReturn**: Amount of resources to be given to the player in case the unit or building is destroyed. Resources aren’t given if the unit or building was deleted, unless the `ApplyResourceReturnIfDeleted` protoUnit flag is set. Takes one parameter, `resourceType`, which sets the resource type.

```xml
<ResourceReturn resourceType="Gold">100.0000</ResourceReturn>
```

- **ResourceReturnRate**: Amount of resources to be given to the player, as a rate of the current cost of the unit, in case the unit or building is destroyed. Resources aren’t given if the unit or building was deleted, unless the `ApplyResourceReturnIfDeleted` protoUnit flag is set. If the protoUnit flag `ResourceReturnRateTotalCost` is set, it will account for the total cost of the unit in resources. If the protoUnit flag `ReturnResourcesOnConstruction` is set, this amount will be given at the end of the construction of the unit instead of its destruction.

```xml
<ResourceReturnRate resourceType="Gold">0.5000</ResourceReturnRate>
```

- **ResourceSubType**: ProtoUnit resource subtype. Used for defining the gather cursor and by resource tasking code.
- **GathererLimit**: Maximum number of units that can gather from this unit at a given time.
- **BuilderLimit**: Maximum number of units that can build this unit when at foundation state at a given time.
- **ResourcePriority**: Factor value for resource prioritization logic, as used when automatically tasking units to gather resources after constructing a dropsite or depleting a resource deposit.
- **WorkerSoftLimit**: Soft worker limit for resource prioritization logic, as used when automatically tasking units to gather resources after constructing a dropsite or depleting a resource deposit.
- **WeightClass**: Relative weight used for pathing pushing logic. Units with a lower `WeightClass` value cannot push units with higher values.
- **SizeClass**: Used for logic for actions that perform knockback effects, which may restrict those to units with `SizeClass` below a provided value within `ProtoAction` data.
- **OnDiscoverLOS**: LOS radius to be temporarily revealed upon discovering this unit.
- **ContainedSpeedBonus**: Defines how much each garrisoned unit contributes to its speed. Applied linearly.
- **ObstructionRadiusX**: ProtoUnit obstruction radius in the X axis.
- **ObstructionRadiusZ**: ProtoUnit obstruction radius in the Z axis.
- **ObstructionRadius**: ProtoUnit obstruction radius in both X and Z axes.
- **SelectionRadiusX**: ProtoUnit in-game selection circle radius in the X axis. Defaults to obstruction radius value for that axis if unset.
- **SelectionRadiusZ**: ProtoUnit in-game selection circle radius in the Z axis. Defaults to obstruction radius value for that axis if unset.
- **AllowedHeightVariance**: Maximum elevation height variance allowed over the area where the protoUnit is to be placed.
- **WanderDistance**: Wandering distance value used by huntable herds.
- **AutoBuildRate**: Rate (buildpoints per second) in which building will be auto-built.
- **BuildPoints**: Total amount of time in seconds required to build protoUnit, when working at a `1.0` Work Rate over the Foundation.
- **BuildingWorkRate**: Work rate value used by training, researching, and maintaining (auto-spawn) actions.
- **TrainingRate**: Work rate multiplier for training actions.
- **ResearchRate**: Work rate multiplier for research actions.
- **RechargeTime**: Recharge time used by Charged Actions/Abilities.
- **ChargeUsageTime**: Amount of time in seconds in which the main Charged Action is usable, after it’s first triggered.
- **AuxRechargeTime**: Recharge time used by Secondary Charged Actions/Abilities.
- **AuxChargeUsageTime**: Amount of time in seconds in which the secondary Charged Action is usable, after it’s first triggered.
- **Recharge**: Recharge value used by Primary Charged Actions/Abilities, which can be tied to variables other than elapsed time. Takes two parameters, `type` and `init`:
  - `type`: Defines the variable to be taken into account for charging. Can be set to `Time`, `Kills`, `Damage`, `Attacks`, or `ResourceDropoff`.
  - `init`: Should be set to `1` if the Charged Ability is supposed to start charged and ready to use as soon as the unit is created, or `0` otherwise. Defaults to `1`.
- **AuxRecharge**: Recharge value used by Secondary Charged Actions/Abilities, which takes the same parameters as `Recharge`.
- **RechargeIncludeTypes**: Allowed target unit types for kill, hit and damage-based action recharging.

```xml
<RechargeIncludeTypes>
  <UnitType>NavalUnit</UnitType>
</RechargeIncludeTypes>
```

- **RechargeExcludeTypes**: Forbidden target unit types for kill, hit and damage-based action recharging.

```xml
<RechargeExcludeTypes>
  <UnitType>NavalUnit</UnitType>
</RechargeExcludeTypes>
```

- **CorpseDecayDelay**: Delay time in seconds before decay “sinking” happens after unit death.
- **Lifespan**: ProtoUnit lifespan.
- **Decay**: Defines the delay time and the duration of decaying fadeout. Takes two parameters, `delay` and `duration`.

```xml
<Decay delay="0.0000" duration="2.0000"></Decay>
```

- **CreationFadeTime**: Time in seconds in which unit will fade in upon being created or placed. Takes one parameter, `initAlpha`, which defines the initial alpha level for the unit once it’s spawned.

```xml
<CreationFadeTime initAlpha="0.0000">1.0000</CreationFadeTime>
```

- **PopulationCapAddition**: Amount of population capacity to be added to the player’s population once protoUnit is fully built.
- **DeadReplacementLifespan**: Deprecated.
- **DeadReplacement**: ProtoUnit to be placed upon destruction.
- **BuildReplacement**: ProtoUnit that replaces originally placed unit once it’s fully built.
- **Spawn**: Defines a protoUnit to be placed according to a set event. Parameters:
  - `type`: Defines the event that will trigger the spawning. Can take one of: `dead`, `killed`, `birth`, `build`, `mutate`, `hit`, `hitGround`, `revertToSocket`, `hitWater`, `selfDestruct`.
  - `count`: Sets the number of units to be spawned.
  - `lifespan`: Sets the lifespan time of the units to be spawned.
  - `chance`: Sets the chance for the spawning to occur. Should be a floating-point value between `0` and `1`.
  - `delay`: Sets the delay, in milliseconds, for the spawning to occur as soon as the set event is detected.
  - `skipPlacementCheck`: If present, placement logic will be ignored for spawned units.
  - `controlGroup`: If present, spawned units will inherit the same control group as the source unit.
  - `waterProtoUnit`: Defines the protoUnit to be spawned if the source unit is over water.
  - `setOwner`: Unknown effect. Appears to modify ownership of the spawned unit.
  - `shadingType`: Apply a shading at birth to the spawned protoUnit. Shading wears off after the birth action completes.
  - `followRallyPoint`: Allows spawned units to follow any set rally point, if one exists.
- **Replacement**: Defines a protoUnit that the unit is to be transformed to as soon as a set event occurs. Parameters:
  - `type`: Defines the event that will trigger unit transformation/mutation. Can take the same values as for `Spawn`, except for `mutate`.
  - `lifespan`: Sets the lifespan time of the resulting unit.
- **BuildLimit**: ProtoUnit build limit.
- **SharedBuildLimitUnit**: ProtoUnit whose build limit value should be used for shared build limit. Requires protoUnit flag `UseSharedBuildLimit` to be set for proper functionality.
- **SharedBuildLimitUnitTypes**: Lists unit types or protoUnits which should be accounted for the shared build limit. Requires protoUnit flag `UseSharedBuildLimit` to be set for proper functionality. `UnitType` entries can take a `weight` parameter that determines how much units belonging to that unitType contribute to the total build limit calculation.

```xml
<SharedBuildLimitUnitTypes>
  <UnitType>Kuafu</UnitType>
  <UnitType weight="0.3334">VillagerChinese</UnitType>
</SharedBuildLimitUnitTypes>
```

- **DynamicBuildLimitUnitTypes**: Lists unit types or protoUnits whose counts will affect the build limit of the unit in a multiplicative manner.

```xml
<DynamicBuildLimitUnitTypes>
  <UnitType>HesperidesTree</UnitType>
  <UnitType>SummoningTree</UnitType>
</DynamicBuildLimitUnitTypes>
```

- **MaxContained**: Maximum number of garrisoned units allowed.
- **ProjectileProtoUnit**: Projectile unit to be used for ranged attack actions that do not explicitly define a projectile unit. Superseded by `ProtoAction`-specific `projectile` protoUnit attribute.
- **ResourceDecay**: Resource decay rate for dead herdables and huntables.
- **SocketUnitType**: Unit type where the protoUnit can be placed over, serving as a socket for the unit.
- **NonSocketPlaceProtoID**: ProtoUnit to be placed down when attempting to construct this unit outside its intended socket.
- **SocketOffsetX**: Offset for socket placement in the X axis.
- **SocketOffsetZ**: Offset for socket placement in the Z axis.
- **AutoAttackRange**: ProtoUnit auto-attack range.
- **GodPowerBlockRadius**: Radius in which unit will cause God Power casting to be blocked.
- **StealthDetectionRadius**: Radius in which units under stealth within the vicinity of this unit will be revealed. Does not reveal units put under stealth through the Vanish God Power.
- **ProjectileSpinPeriod**: Spinning period for projectile protoUnits. Causes projectiles to spin at the set period when shot.
- **HeightBob**: Causes the altitude of flying units to slightly oscillate by periods of time while idle. Takes two parameters, `period` and `magnitude`.

```xml
<HeightBob period="6.0000" magnitude="2.0000" />
```

- **PartisanType**: Partisan protoUnit to be spawned upon building’s destruction, if partisans are enabled in the current civilization.
- **PartisanCount**: Amount of partisan units to be spawned, if `PartisanType` is properly set and partisans are enabled in the current civilization.
- **BallisticSplashProto**: ProtoUnit for SFX to be rendered upon projectile colliding on water. Unused and superseded by impact effect data.
- **BallisticImpactProto**: ProtoUnit for SFX to be rendered upon projectile colliding against structures. Unused and superseded by impact effect data.
- **ImpactType**: ProtoUnit impact type. Used for impact effects rendering.
- **ScreenshakeOnDestruction**: Strength value to be applied for camera shaking upon full destruction.
- **DependentUnit**: Defines a protoUnit to be spawned upon unit creation/placement, that will be removed as soon as the source unit is destroyed. Parameters:
  - `x`: Unit placement offset on the X axis.
  - `y`: Unit placement offset on the Y axis.
  - `z`: Unit placement offset on the Z axis.
  - `attachbone`: Unit placement on the unit's bone, if it exists.
- **PhysicsInfo**: Path to protoUnit physics info files, relative to `Data\physics`.
- **SelectionPriority**: ProtoUnit selection priority value.
- **Culture**: Culture to be used for object filtering in the Scenario Editor.
- **Armor**: ProtoUnit Armor. Takes two parameters, `type` and `value`.

```xml
<Armor type="Pierce" value="0.7500"></Armor>
```

- **DirectionalArmor**: Multiplier to be applied to damage dealt against the unit within a provided angle in radians. Takes two parameters, `angle` and `value`.

```xml
<DirectionalArmor angle="1.0472" value="0.5000" />
```

- **ReceiveDamageMultiplier**: Multiplier to be applied to all damage dealt against this unit.
- **Train**: Adds a unit to the protoUnit’s command panel. Takes two parameters: `row` and `column`.

```xml
<Train row="0" column="3">Hoplite</Train>
```

- **Tech**: Adds a tech to the protoUnit’s command panel. Takes the same parameters as `Train`.

```xml
<Tech row="0" column="1">HeavyArchers</Tech>
```

- **Command**: Adds a `ProtoUnitCommand` to the protoUnit’s command panel. Takes two parameters: `page` and `column`.

```xml
<Command row="3" column="2">SeekShelter</Command>
```

- **OptionalCommand**: Adds a `ProtoUnitCommand` to the protoUnit’s command panel, which will only be displayed when having a unit within the selection that has that command. Takes two parameters: `page` and `column`.

```xml
<OptionalCommand row="2" column="4">MilitaryCampToTower</OptionalCommand>
```

- **Build**: Adds a unit to the protoUnit’s command panel. Takes two parameters: `row` and `column`.
- **Contain**: Allows the set unit type to be garrisoned within the unit. Parameters:
  - `external`: If set to `1`, contained units will be rendered outside containing building or unit.
  - `inDelay`: Delay time in seconds for garrisoning units of the given unit type.
  - `outDelay`: Delay time in seconds for ejecting units of the given unit type. Requires `MeteredGarrison` protoUnit flag to be set in container for proper functionality.
- **Tactics**: Path to protoUnit tactics file, relative to `Data\tactics`.
- **HotkeyContext**: Hotkey context to be used for hotkey keybinding. Should be set to a valid hotkey context.
- **AllyHotkeyContext**: Hotkey context for hotkey keybinding accessible by allied players. Should be set to a valid hotkey context.
- **HoverTextOverride**: Deprecated.
- **BuildTextOverride**: String ID to be used for the notification text once the building is fully built. Uses default hardcoded formatted string if not set. Only functional if `TrainBuildFeedback` config is set.
- **ContainedHitPointBonus**: Defines how much each garrisoned unit contributes to its hit points. Applied linearly.
- **PlacementObstructionRadiusX**: Obstruction radius to be used while placing foundations in the X axis. Used by buildings with crops, whose placement is restricted by `ObstructionAtLeastFromType` conditions.
- **PlacementObstructionRadiusZ**: Obstruction radius to be used while placing foundations in the Z axis. Used by buildings with crops, whose placement is restricted by `ObstructionAtLeastFromType` conditions.
- **FarmingRadiusX**: Radius of the walkable area of a farm building in the X axis. Requires the `UseFarmingAnims` protoUnit flag to be set for proper functionality.
- **FarmingRadiusZ**: Radius of the walkable area of a farm building in the Z axis. Requires the `UseFarmingAnims` protoUnit flag to be set for proper functionality.
- **FarmingNumStops**: Number of different positions a unit can move to after finishing a gathering cycle in a farming building, including its current position. Can’t be set to a value lower than `2`. Requires the `UseFarmingAnims` protoUnit flag to be set for proper functionality. If it’s not set, it defaults to the hardcoded value of `8`.
- **FarmingObstructionRadiusX**: Obstruction radius in the X axis to be used by farming code. Doesn’t affect farm placement or building.
- **FarmingObstructionRadiusZ**: Obstruction radius in the Z axis to be used by farming code. Doesn’t affect farm placement or building.
- **DodgeChance**: Chance of dodging an attack. Defaults to a hardcoded value if not set. Requires protoUnit flag `CanDodgeAttacks` to be set for proper functionality.
- **DodgeMessageID**: String ID of the floating text message to be used when successfully dodging an attack. Defaults to a hardcoded value if not set. Requires protoUnit flag `CanDodgeAttacks` to be set for proper functionality. Works with AoM:Retold based on the ID of the string, i.e. its position in the string table (example: ID `1` is “Greeks”).
- **DodgeSoundSet**: Soundset to be played when successfully dodging an attack. Defaults to a hardcoded value if not set. Requires protoUnit flag `CanDodgeAttacks` to be set for proper functionality.
- **ConversionResistance**: Multiplies the conversion time for this unit by the given value.
- **DisplayedRange**: Overrides the displayed range value by the given value. Requires protoUnit flag `DisplayRange` to be set for proper functionality.
- **GatherRateMultiplier**: Rate by which the gather rate of a unit gathering from an instance of this protoUnit will be multiplied.
- **NotContain**: Forbids the set unit type from being garrisoned within the unit.
- **DeadTransform**: ProtoUnit to which the current unit will be transformed upon reaching zero hitpoints.
- **AIStanceBaseDistance**: Base distance to be used for unit auto-attack reaction when its AI Stance is set to Defensive. Set to `30` by default.
- **ContainedRegenRate**: Percentage-based regeneration rate for garrisoned units within the protoUnit. Only applies for buildings; overrides `DefaultContainedRegenRate` in base civilization data.
- **FreeBuildPoints**: Deprecated.
- **SocketBuildProtoUnit**: ProtoUnit to be built as a socketed building over the current unit when a socketBuild command is applied.
- **SocketBuildRate**: Rate in which a socketBuild command will build the building referred through the SocketBuildProtoUnit attribute.
- **DeploymentCommand**: ProtoUnit command used for deployment of units currently garrisoned and that effectively replaces the default Ungarrison command. Requires DeploymentUngarrison protoUnit flag to be set for proper functionality.
- **VeterancyRanks**: Defines veterancy rank data for this protoUnit. Each rank is defined through a `Rank` child node, which holds the core data for veterancy progression for that rank. Each `Rank` child node can take the following child nodes:
  - `NumKills`: Number of kills required for progressing to the provided rank.
  - `NumAttacks`: Number of attacks required for progressing to the provided rank.
  - `TotalDamage`: Total damage required for progressing to the provided rank.
  - `DamageAndResourcesEaten`: Total damage and resources eaten through Eat protoaction for progressing to the provided rank.
  - `Active`: Determines whether this `Rank` entry is to start as active or not. When set to `0`, it will need to be enabled by a technology. All ranks start as active by default. Note that if a rank is inactive, all ranks after it will be too.
  - `StringID`: Displayed string ID for the provided rank. Currently not used by UI.
  - `Icon`: Icon path for the provided rank. Currently not used by UI.
  - `Resetnumkills`: Reset the numkills counter of the unit upon reaching this rank.
  - `ResetRank`: Reset to the first rank.

```xml
<VeterancyRanks>
  <Rank>
    <TotalDamage>50</TotalDamage>
  </Rank>
  <Rank>
    <TotalDamage>150</TotalDamage>
  </Rank>
  <Rank>
    <TotalDamage>300</TotalDamage>
  </Rank>
</VeterancyRanks>
```

- **VeterancyBonus**: Defines bonuses to be applied to the unit upon reaching specific veterancy ranks. Bonuses for each rank are defined through `VeterancyModify` child nodes, which take a `modifyType` attribute that should be set to a valid modify type. Requires `ExperienceUnit` protoUnit flag to be set for proper functionality. Valid and restricted targets for veterancy progression can be defined through `IncludeTypes` and `ExcludeTypes`, respectively.

```xml
<VeterancyBonus>
  <Rank id="0">
    <VeterancyModify modifyType="MaxHP">1.15</VeterancyModify>
    <VeterancyModify modifyType="ROF">0.85</VeterancyModify>
  </Rank>
  <Rank id="1">
    <VeterancyModify modifyType="MaxHP">1.30</VeterancyModify>
    <VeterancyModify modifyType="ROF">0.70</VeterancyModify>
  </Rank>
  <Rank id="2">
    <VeterancyModify modifyType="MaxHP">1.45</VeterancyModify>
    <VeterancyModify modifyType="ROF">0.55</VeterancyModify>
  </Rank>
  <IncludeTypes>
    <UnitType>Unit</UnitType>
  </IncludeTypes>
  <ExcludeTypes>
    <UnitType>AbstractVillager</UnitType>
  </ExcludeTypes>
</VeterancyBonus>
```

- **VeterancyRankUp Spawn**: Can also spawn a unit upon reaching a rank through a `spawn` attribute. Takes the same parameters as the standard `Spawn` attribute with two special parameters:
  - `param`: Defines the rank ID to reach to spawn the unit.
  - `swapKilledUnit`: Swaps the killed unit with the spawn. Note that it keeps the appearance of the original unit and cannot attack.

```xml
<spawn type="veterancyRankUp" count="1" param="1" skipplacementcheck="" swapkilledunit="" shadingtype="gold">MineGoldMidas199</spawn>
```

- **OnDamageModifiers**: Defines progressive modifiers to be applied as the unit gets damaged. The amount to be added for a modifier for each percent of the total unit’s HP lost is defined through an `OnDamageModify` child node, which can take the following parameters:
  - `modifyType`: Modify type for the modifier to be applied.
  - `damageType`: Damage type for `ArmorSpecific` and `DamageSpecific` modify types.

```xml
<OnDamageModifiers>
  <OnDamageModify modifyType="Damage">0.0025</OnDamageModify>
</OnDamageModifiers>
```

- **ModifyTiers**: Defines the total rate values to be used for progressing to a given tier through child `Tier` nodes. Intended to be used by the modification tiers system for self-modification protoActions.

```xml
<ModifyTiers>
  <Tier>5</Tier>
  <Tier>10</Tier>
</ModifyTiers>
```

- **TransformCommand**: ProtoUnit command to be researched when a transform command is issued over the unit.
- **SelfDestructProtoAction**: Attack protoAction to be triggered upon death.
- **BirthProtoAction**: Attack protoAction to be triggered upon completion of birth animation.
- **StackProtoAction**: ProtoAction to be used for stackable effect management (e.g. Taotie unit devouring mechanic).
- **PathabilityFlags**: For walls and gates, defines their path ability. `Wall|Air` blocks units excluding flying ones. `BlocksAll` blocks every unit type.

```xml
<pathabilityflags>Wall|Air</pathabilityflags>
```

- **RespawnTrainData**: Defines a unit that can respawn at another protoUnit or, if `respawnTypes` is defined, a unit that respawns the target unit. Takes the following parameters:
  - `active`: Self-explanatory. Set to `1` by default.
  - `targetType`: Defines the target as the respawn point for this protoUnit.
  - `trainProto`: Defines the protoUnit to be trained upon the death of the original protoUnit.
  - `respawnTime`: Time in seconds for the respawn to happen.
  - `respawnVFX`: Self-explanatory.
  - `respawnTypes`: Makes the protoUnit respawn other units and defines which type of units can be respawned. Takes one parameter, `unitType`. The protoUnit to be trained needs to have the flag `RespawnTrainOnDeath` set to active.
  - `excludeTypes`: If the protoUnit respawns other units, defines which type of units is excluded from the pool. Takes one parameter, `unitType`.
  - `respawnRates`: Defines the rate at which a unit should be respawned according to its resource cost, in seconds. Has four child nodes, one for each resource.

```xml
<respawnrates>
  <food>0.5000</food>
  <wood>0.5000</wood>
  <gold>0.5000</gold>
  <favor>0.5000</favor>
</respawnrates>
```

  - `respawnLimit`: Defines the max number of units at once that can be in queue from the protoUnit.
- **GodPowerCostFactor**: Defines the cost reduction for current and future godpowers when owning the protoUnit.
- **DisguiseProtoid**: Defines the protoUnit model to be used as disguise when viewed by the enemy.
- **EidolonProtoid**: Defines the protoUnit to be invoked as eidolon when Underworld Invasion godpower is used.
- **EnemyShortRolloverTextID**: String ID for protoUnit short rollover for the enemy.
- **BloodGroupOverride**: Overrides default blood pool of the unit. Can be any `bloodgroup` defined in `blood.xml`.
- **BoneScaleModify**: Applies a multiplier to the size of the skeleton when the unit dies.
- **BloodScaleModify**: Applies a multiplier to the size of the blood pool when the unit dies.
- **StealthRevealSelfRadius**: Radius at which the unit will break its stealth and start attacking.
- **StealthShowSilhouetteRadius**: Radius at which the silhouette of the unit will be visible to enemies even without stealth detection. The unit will remain stealth until it exits this status by revealing itself or being revealed by going under its `StealthRevealSelfRadius` limit.
- **ResourceConversion**: Convert a percentage of a resource type deposited into this unit to another resource at this rate. Takes two parameters: `fromResourceType` and `toResourceType`.

```xml
<resourceconversion fromresourcetype="Food" toresourcetype="Gold">0.1</resourceconversion>
```

- **DecayTime**: Same as `duration` for `Decay` attribute.
- **DecayDelayTime**: Same as `delay` for `Decay` attribute.

## Flags

- **NoUnitAI**: Self-explanatory.
- **NotPlayerPlaceable**: Causes the unit to not be directly placeable through the Editor.
- **StartEnabled**: Unit starts enabled, without the necessity of being explicitly enabled by any technology.
- **NotAlive**: Self-explanatory.
- **TieToWaterSurface**: Self-explanatory.
- **FlyingUnit**: Self-explanatory.
- **NoTieToGround**: Self-explanatory.
- **Collideable**: Self-explanatory. Set by default.
- **NonCollideable**: Self-explanatory.
- **Immoveable**: Self-explanatory.
- **NoHPBar**: Self-explanatory.
- **DieAtZeroHitpoints**: Self-explanatory. Set by default.
- **DoNotDieAtZeroHitpoints**: Self-explanatory.
- **DieAtZeroResources**: Self-explanatory.
- **DoNotDieAtZeroResources**: Self-explanatory.
- **ValidateResourceInventory**: Forces the game to verify current unit resource inventory against the carry capacity for each resource, and adjust it accordingly. Set by default.
- **DoNotValidateResourceInventory**: Causes unit resource inventory to not be checked against the carry capacity for each resource.
- **NoBloodOnDeath**: Self-explanatory.
- **BloodOnDeath**: Self-explanatory. Set by default.
- **DoesNotHaveGatherPoint**: Self-explanatory. Set by default.
- **HasGatherPoint**: Self-explanatory.
- **PlayerPlaceable**: Set by default.
- **NonSolid**: Causes unit or building obstruction to not block units from passing through.
- **Selectable**: Self-explanatory. Set by default.
- **NotSelectable**: Self-explanatory.
- **FlattenGround**: Self-explanatory.
- **ObscuresUnits**: Self-explanatory.
- **ObscuredByUnits**: Self-explanatory.
- **NotObscuredByUnitsAsFoundation**: Self-explanatory.
- **DoNotShowOnMinimap**: Self-explanatory.
- **NonAutoFormedUnit**: Causes units to not adopt formations automatically.
- **DontRotateObstruction**: Causes actual obstruction to be rotated according to building orientation.
- **CreateUnitGroupAutomatically**: Causes unit to automatically be added to a Squad once it’s instantiated.
- **VisibleUnderFog**: Self-explanatory.
- **VisibleUnderFogIfNature**: Self-explanatory.
- **AlphaFadeLifespan**: If `Lifespan` is set for the protoUnit, causes the fade-out to begin as the lifespan time starts to be counted.
- **Wanders**: Causes units to wander. Used for herdables and huntables. Seems to only affect GAIA-owned units.
- **CollidesWithProjectiles**: Self-explanatory.
- **Projectile**: Self-explanatory.
- **FadeInOnBuild**: Causes buildings to have a fade-in effect after being fully built.
- **NotSearchable**: Causes the unit to not be accounted for internal visible unit lookups.
- **UnlimitedSupply**: Used for resource storages with unlimited supply of resources.
- **FaceOutwards**: Causes unit to be placed facing the lowest terrain point.
- **SnapPlacement**: Allows socketed buildings to properly snap into sockets during placement.
- **FadeOutDuringDeathAnimation**: Causes fade out to start as the death animation begins.
- **ForceToNature**: Self-explanatory.
- **DoNotYawDuringMovement**: Intended to cause units to not rotate/turn while moving.
- **GivesLOSToAll**: Self-explanatory.
- **Doppled**: Causes the unit to leave a doppelganger when under fog.
- **NotDeleteable**: Self-explanatory.
- **DestroyProjectile**: If set on a projectile protoUnit, causes it to be destroyed after reaching the target.
- **OnlyInEditor**: Self-explanatory.
- **CannotAttackDisabledUnits**: Unused and deprecated.
- **OrientUnitWithGround**: Causes unit to orient itself with the ground.
- **AlwaysFullColorAsCursor**: Determines whether or not we check obstructions and alter the color of this as a cursor item.
- **ConstrainOrientation**: Enables orientation constraints for the code that orients a unit with the ground.
- **InitialGarrisonOnly**: If set, unit will only be garrisonable by units trained from it upon setting the gather point to itself.
- **WallBuild**: Self-explanatory.
- **ShowGarrisonButton**: Unused and deprecated.
- **NotCommandable**: Causes unit to not be able to take commands.
- **KillOnAnimLoop**: Causes unit to be killed on next animation loop.
- **AreaDamageConstant**: Causes the area damage inflicted by the unit to not vary with distance from the original attack target position. Unused, but functional.
- **NoIdleActions**: Causes internal idle action to not be processed for this unit.
- **NoProjectileDamage**: Causes projectile unit to inflict no damage.
- **PlaceAnywhere**: Disables placement checks entirely.
- **ProjectileTerrainOnly**: Forbids projectile unit from colliding against units.
- **PlayerOwnsObstruction**: Used for Gate functionality.
- **PlaceSocketWhenPlacing**: Causes Socket protoUnit to be placed once the building is placed. Requires `SocketUnitType` to be set to a protoUnit, instead of a UnitType.
- **AlwaysShowAsSocket**: Causes socket unit to remain visible after it’s occupied.
- **StartOnAnimationUpdate**: Causes unit to be initialized with persistent updates (i.e. for unit AI or persistent actions) disabled, except for animation updating.
- **StartOnNoUpdate**: Causes unit to be initialized with persistent updates (i.e. for unit AI or persistent actions) disabled.
- **DeadReplacementWhenDestroyed**: When set, causes the dead replacement to be only placed when the unit is actually destroyed, and not right after death/killing is triggered.
- **AnnounceConversion**: Causes a notification to be sent to all players when building is upgraded/transformed.
- **SelectWithObstruction**: If set, selection will also account for unit obstruction.
- **ConvertOnStartBuild**: Causes building to be converted to player as soon as upgrading/transforming process starts.
- **PlaceAsFoundation**: Forces building to be not fully built on scenario load or when placed in the editor.
- **ConvertToGaiaAtZeroHitpoints**: Returns object to Gaia control at zero hitpoints.
- **MakeUnbuiltAtZeroHitpoints**: Resets all construction progress when the unit hits zero hitpoints.
- **ExcludeFromPlaytest**: Unused and deprecated.
- **SolidFoundation**: Causes foundations to be solid and collideable at placement.
- **HideGarrisonFlag**: Causes Garrison Flag to not show up over unit/building if it has garrisoned units.
- **DoppleOnlyWhenDead**: If set, unit will only leave a doppelganger under fog when dead. Used for trees.
- **DirectProjectile**: If set, launched projectiles fly direct in a straight line to their target.
- **ForceBuildingData**: If set, causes internal Building Data, containing attributes like Building Work Rate, to be initialized for the unit, even if it’s not a building.
- **DecalStickToWaterSurface**: If set, the decal will be computed using water vertices when over water.
- **AllowAutoGarrison**: Allows auto-garrisoning by right-clicking for garrisonable units.
- **OverrideInitialGarrison**: Unused and deprecated.
- **MeteredGarrison**: Causes ungarrisoning/ejection to be done unit per unit, internally.
- **RevealFoundation**: When set, causes this building's location to be revealed to all when first worked upon.
- **ColorTransformNonNature**: When set, causes the minimap icon to use the player color when unit is converted from Gaia.
- **ApplyHandicapTraining**: Unused and deprecated.
- **NotKBTracked**: Causes the unit to not be accounted for by KB lookups.
- **VisibleOwnerOnly**: Makes the unit become only visible to owner and allies.
- **HideFromDialogs**: Hides unit from listings within unit help dialogues and advanced tech rollovers.
- **HideResourceInventory**: Causes inventory to not be displayed on UI upon selection.
- **NotRotateable**: Forbids object or building from being rotateable at placement.
- **DestroyUnderBuilding**: Causes the object to be deleted once a building foundation is placed over it.
- **NotScalable**: Unused and deprecated.
- **GodPowerExclusion**: Prevents God Powers from being cast under the vicinity of the unit/building, using the default radius for God Power blocking, as set through the `GPShieldRadius` config, or defaulting to `20`, in case it’s not set.
- **Invulnerable**: Self-explanatory.
- **DeadReplaceOnlyOnTimeout**: Limits dead replacement only to deaths due to lifespan expiring.
- **SingleGatherer**: Unused and deprecated.
- **InvulnerableIfNature**: Self-explanatory.
- **CorpseDecays**: Determines if unit is supposed to get corpse decals when it dies.
- **CantBeSlowed**: Forbids unit from being affected by snaring/TargetSpeedBoost.
- **HideHitpointsIfNature**: Hides hitpoints from UI when owned by Nature/Gaia.
- **FlareOnFullyBuilt**: Causes construction to be flared upon completion.
- **AnnounceFoundationStarted**: Causes all players to be notified as soon as construction starts being worked on by villagers.
- **VictoryBuilding**: Unused and deprecated.
- **PaintTextureWhenPlacing**: If set, forces the editor to paint down a suitable texture underneath if required.
- **Burnable**: Unused and deprecated.
- **MutateDopples**: Causes fog of war doppelgangers to be updated, in case base unit got mutated to another unitType.
- **InvalidTownBellLocation**: Prevents building from receiving units for garrison from Town Bell activation.
- **UseObstructionOnMinimap**: Unused and deprecated.
- **UseAlignedObstructionOnMinimap**: Unused and deprecated.
- **DontMarkExtraFog**: Causes unit to not mark additional fog (unveil nearby fogged units).
- **VisibleUnderFogOnlyAfterSeen**: If set, unit will become visible under the fog if it had been seen before by the player.
- **RMCanRotate**: Allows unit to be rotated by RM placement.
- **KnockoutDeath**: Enables hero death for unit.
- **VariationLocked**: Unknown effect.
- **ExperienceUnit**: Causes kills of military units performed by this unit to be internally tracked by the unit’s dynamic data. Required for the usage of the Veterancy system on units.
- **FadeOutDecalOnDeath**: Causes unit decal to fade out upon death.
- **AnnounceDestruction**: Causes a notification to be sent to all players when destroyed.
- **BattleMusicTrigger**: If set, triggers battle music when attacked.
- **RotateInPlace**: If set, allows units to rotate even if they are immovable.
- **AdjustPositionOnTerrainCollision**: If set, this unit will stop moving at the point of impact, and move to the point of intersection.
- **HeroNameSimple**: Causes unit to use randomly generated names, out of names defined in `game\data\strings\random_names.xml`.
- **HeroNamePharaoh**: Causes unit to use randomly generated names, out of names defined in `game\data\strings\random_names.xml`, while adding a roman numeral after its name, after dying and being re-spawned, and adding titles and epithets, if those happen to be defined within the aforementioned file.
- **HideCostFromDetailHelp**: Unused and deprecated.
- **PreventsWallBuilding**: Should be set true for buildings/objects that won't allow a wall nearby. Unused, but functional.
- **CreateUniqueInstance**: Causes every instance of this unit to use its own instance of protoUnit data. Not useful for AoM: Retold.
- **TileAlignPlacement**: If set, item snaps to tile aligned locations when placing.
- **WorldToolTip**: Unused and deprecated.
- **TCBuildLimit**: Causes unit to use shared TownCenter Build Limit.
- **Blocker**: Unused and deprecated.
- **LockedSquad**: Not applicable for AoM: Retold.
- **SelectOnTrain**: Unused and deprecated.
- **PlaceAnywhereRules**: Forces building to abide by placement rules, even if `PlaceAnywhere` protoUnit flag is set.
- **ForcePopulationImpactWhenPlaced**: Enforces population impact right when building foundation is placed.
- **CanAutoHeal**: Specifies units that can auto-heal other units.
- **ExcludeFromMoveAllMilitary**: Self-explanatory.
- **DoNotShowAutoGatherRate**: Unused and deprecated.
- **CanTargetButTakesNoDamage**: Self-explanatory.
- **AllowOverPopCap**: Allows unit to be spawned from a Maintain action, if there’s at least one free population slot, regardless if player will go over population capacity afterwards.
- **EnterHotkeyContext**: Unused and deprecated.
- **CivSpecificText**: Allows this unit to properly use civilization-specific text in its tooltip, based on civ keys.
- **AlwaysAllowOverPopCap**: Forces unit to be spawned from a Maintain action, even if there are no free population slots.
- **NeverCountDeathAsLoss**: Causes unit’s death to not be counted as loss for stat tracking.
- **BuildingShowTactics**: Causes building tactics defined through tactic data to be displayed in the UI.
- **DisplayRange**: Causes unit range to be displayed as a decal upon selection. Rendered obsolete with recently implemented game options, which allow displaying range for all units/buildings.
- **InvulnerableToAreaDamage**: If set, this unit cannot receive any kind of damage from area attacks.
- **DoNotDragSelectWithUnits**: If set, this unit won't be selected with other UnitClass units when drag-selecting.
- **TownDefenseUnit**: Intended to denote short-duration levied units. Obsolete.
- **DontTrainInBatches**: Prevents batch training and forces train limit per action to `1`.
- **KillIfConverted**: If set, unit is automatically killed after being successfully converted/captured.
- **ShowUnitResourceActionRates**: Unused and deprecated.
- **SettlerBuildLimit**: If set, unit will share build limit with all units with the `LogicalTypeVillagerBuildLimit` unit type set.
- **UseSharedBuildLimit**: If set, unit will use the generic shared build limit.
- **InflictsNoDamage**: If set, unit should not inflict any damage when attacking, regardless of protoAction attributes. Attacks performed by this unit will still raise warnings for enemies.
- **CanDodgeAttacks**: Enables dodging behaviour for non-Japanese Monk units.
- **NextResearchIsFree**: Forces the immediate next research to be added to the queue in a building to be free.
- **UnitTransformFree**: If set, transforming to this unit won't cost any resources.
- **UseFarmingAnims**: If set, units will move around the gather site while gathering from it, akin to Mills and Farms.
- **BuiltWithSeedingAnim**: If set, forces units to use farming animations when constructing the building.
- **RangeDisplayedAsSquare**: Unused and deprecated.
- **AllowSocketPlacement**: Indicates units that behave like sockets, while not belonging to the `AbstractSocket` type.
- **OptionalSocketPlacement**: If set, a socket-able building will still be buildable outside a socket.
- **AllowPlacementOnIce**: If set, allows a building to be placed over Ice terrain.
- **GatherableWhenSocketed**: Intended for buildings which become gatherable when placed over a socket.
- **DoNotQueue**: If set, unit won't obey building queue when trained.
- **MagnetDoesNotLockUnits**: If set, magnet building won't make herdables/huntables unattackable.
- **UseTacticArmorOverride**: If set, unit will check for armor overrides in tactic data.
- **ResourceReturnRateTotalCost**: If set, return resource rate will be calculated over the total cost, instead of per resource.
- **DoApplyResourceReturnIfDeleted**: If set, resource return will be applied even if the unit was deleted by the player.
- **GatherableByAllies**: If set, allows non-standard resource buildings to be gathered by allies.
- **ShowAutoGatherAbsoluteInfo**: Unused and deprecated.
- **DoTacticToSameUnitType**: If set, changing tactic for this unit will cause the change to be propagated to all instances of the same unit, akin to Japanese Shrine behaviour.
- **CannotSnare**: If set, unit won’t be able to cause enemy units to become slower temporarily (i.e. ‘snaring’ them) through attack actions with the `TargetSpeedBoost` protoAction flag set.
- **DoNotUseBaseSpeedRunAnim**: If set, the base protoUnit speed will not be used as a reference for switching to running animation and calculating animation speed for movement.
- **DeadTransformBuildLimit**: If set, when a unit transform is triggered upon unit death, through the `DeadTransform` protoUnit attribute, build limit will be checked for the target unit.
- **ForceGatherSiteResource**: If set, the game will always use the gather site inventory resource ID for gathering, set through `ModifyGather` unit actions, without checking the protoUnit main resource or the unit inventory itself.
- **UseStaticFarmingAnims**: If set, units will gather from this gather site at predefined spots, defined as bones within the building model.
- **GatherGarrisonToggle**: If set, allows building to toggle between gathering and garrisoning mode.
- **HerdablesIgnoreGatherPoint**: If set, herdables created/spawned from the unit will ignore gather point data.
- **FreeRepair**: If set, repairing this unit costs no resources.
- **CountHerdableAsGatherer**: If set, herdables gathering at this unit will be counted as gatherers by the game KB.
- **GatherersContributeToResourceRate**: Unused and deprecated.
- **AllowGatheringWhenFull**: If set, full inventory checks will be disabled when gathering from this unit, allowing herdables to gather from this unit, even if their current resource inventory is full.
- **HideHealAttachment**: If set, heal indicator attachment won’t be displayed whenever a healing action or passive healing bonus is performed over the unit.
- **ChargeMoveAnim**: If set, unit will be allowed to use custom animations when moving to perform a charged action.
- **SocketFreeBuilding**: If set, issuing a valid SocketBuild command over this unit won't deduct building cost from the player's stockpile.
- **CannotAttackIfNature**: If set, unit won't be able to attack when belonging to GAIA.
- **FakeConversion**: If set, placing or converting the unit to a player won’t change actual ownership of the unit, but will still set the given player as the owner for resource production through `AutoGather` protoActions.
- **UseAltRepairCursor**: If set, Ankh cursor will be used for repairing/building actions.
- **ForceUpdateVisualWhenConverted**: If set, full unit visual update will be enforced upon conversion.
- **MinimapDisplayOnTop**: If set, this unit's minimap icon will always be forced to be displayed on top of all other units within its vicinity.
- **NotRepairable**: Prevents this building from being repaired when set.
- **KillSocketWhenDestroyed**: If set, unit socket will be removed upon destruction.
- **TeamBuildLimit**: If set, build limit logic will account for unit count throughout all team players.
- **IgnoreDefaultEjectTimeout**: If set, unit ejection action won't check, internally, for the default unit AI eject delay, if `MeteredGarrison` protoUnit flag is set.
- **DoNotQueueEjectActions**: If set, alongside the `MeteredGarrison` protoUnit flag, allows multiple units to be ejected at the same time, even if they have ejection delays set.
- **SharedGarrison**: If set, garrisoned units will be shared throughout all other instances of the current protoUnit, and of other protoUnits which have this flag set.
- **DisplayMinimumRange**: If set, range decal will also display the minimum attack range of the unit.
- **DoNotAllowAllowAlliedGarrison**: If set, allies won’t be able to garrison within this unit.
- **ForceNormalDeathAnim**: If set, building will use the same handling for death animations used by units.
- **DeploymentUngarrison**: If set, units won't be able to be ungarrisoned manually, but only through a previously-set deployment ability/command.
- **ForceDisplaySquadModes**: Unused and deprecated.
- **HideIfSocketedFoundationUntouched**: If set, socket will be hidden if building placed upon it is currently an untouched foundation.
- **PrayableTo**: If set, praying animation will be used when gathering from this target and dropsite gathering behavior will be overridden.
- **UpgradeSocket**: If set, causes building to ‘upgrade’ the socket it’s placed over (i.e. Settlement construction behavior).
- **RevertToSocketAtZeroHitpoints**: If set, reverts to original socket once destroyed.
- **Relic**: Self-explanatory.
- **KillsTargetAfterPickupAction**: If set, causes targets picked up by Throw actions to die as soon as Throw action event is detected.
- **PlaySoundOnConversion**: Unused and deprecated.
- **NoAllyRepair**: If set, prevents allies from being able to repair this unit.
- **DestroyWhenCompleted**: If set, causes building to be destroyed upon being fully constructed.
- **CommonCommands**: If set, common unit commands (Ungarrison, Stop and Delete) will be displayed on the 4th row.
- **MilitaryCommands**: If set, common military commands (Patrol, Attack Move) will be displayed on the 4th row, as well as the common unit commands.
- **DeleteConfirmation**: If set, deletion confirmation dialog will be displayed whenever attempting to delete this unit, regardless of unit type or number of units selected.
- **AutoTrainable**: If set, unit will be auto-queueable, even when the ‘Enable Military Auto-Queue’ setting is not enabled.
- **ExcludeFromIdleQuery**: If set, unit will be ignored by all queries for idle units performed by hotkeys or in-game UI.
- **HideStances**: If set, unit stances will be hidden from in-game UI.
- **RecoverableDeathHeal**: If set, unit will be able to recover from death by being healed.
- **RecoverableDeathProximity**: If set, unit will be able to recover from death by friendly proximity.
- **NotAttackableByNature**: If set, unit will not be attackable by Nature/Gaia-owned units.
- **NoLockedAnimationOnDeath**: If set, unit will not lock animations on death to allow any `TMCompositeModel` to play out events on the model side.
- **NotEjectable**: If set, unit will be forbidden from ejecting any garrisoned units by itself.
- **HasContinuousParticles**: If true, unit's particle attachments' transforms must be updated whenever the unit moves, otherwise the particles will spawn incorrectly when the unit reappears on screen.
- **HideGarrisonCapacity**: If set, protoUnit garrison capacity will be hidden from in-game UI.
- **OrientUnitWithGroundRoll**: If set, causes unit to orient itself with ground taking only the roll angle into account, with no changes to pitch angle.
- **DisplayRangeOverride**: If set, unit range indicator will be displayed when selecting the unit, regardless of game settings. Also causes the range for all nearby units with this flag set to be displayed, both when placing down and selecting the unit.
- **AutoCommandStartDisabled**: If set, abilities with the `ActionCommand` flag will start with auto-casting disabled.
- **AutoScout**: If set, causes Auto Scout to be displayed as a command within in-game UI.
- **DependentAttack**: If set, causes dependent units to attack together with parent unit when issuing an attack command.
- **PreQueueNotAllowed**: If set, unit won’t be prequeueable.
- **Pickable**: If set, will allow unit to be pickable by Pickup actions, without enforcing all of the behavior of Relics.
- **ShowUntouchedFoundation**: If set, building foundation will be visible to all players, even while on untouched (i.e. 0% completion) state.
- **SelfRespawn**: If set, causes unit to re-spawn.
- **NotDeleteableFoundation**: When set, prevents unit from being deleteable while on foundation state.
- **RangeIndicatorForAbilityCasting**: When set, units with an ability will show a range indicator while being in manual ability casting mode.
- **NotPushable**: When set, prevents unit from being ‘pushable’ by pathing logic.
- **PushPlacement**: When set, unit will push units within its vicinity upon placement.
- **HasDefaultAttack**: When set, activates lookup for the default attack (i.e. for an attack protoAction with the `DefaultAttack` flag set), when displaying any attacks on the in-game UI.
- **SkipFlyingDeathAnim**: If set, special logic for flying unit death will be ignored, and death animation will be played normally.
- **DisplayOnMinimapIfContained**: If set, minimap icon for this unit will be displayed even when contained/garrisoned.
- **NotDeathTracked**: If set, unit death won’t be tracked by game KB.
- **ContainedLOS**: If set, unit will still have active LOS even while contained/garrisoned.
- **ForceSecondPage**: If set, second page toggle will be displayed on game UI, even if no unit training or building construction is assigned to the unit.
- **ForceEmpowerable**: If set, forces a fully-built building to be empowerable, even if it doesn't have any trainable units or researches.
- **KeepGarrisonTimeshift**: If set, garrisoned units will be kept when performing time-shifting.
- **GroupSelection**: If set, unit will be grouped on the selection panel with other protoUnits it shares selection with.
- **UnitTransformBuildLimit**: If set, transformation triggered by protoUnitCommands will take into account build limit for this unit.
- **NotManuallyPlaceable**: When set, unit won’t be placeable through in-game build commands.
- **ConvertToTOB**: If set, unit will be replaced by a terrain object at game simulation start.
- **MilitaryUIDefault**: If set, military commands page will be displayed by default when using economic/military page toggling.
- **DynamicUpdate**: If set, the unit tries to return to no update mode (preventing it from going through full-update every game frame) when possible and always has an AI.
- **DynamicUpdateAnimate**: Requires `DynamicUpdateMode` to be set. When set, unit will use animate update instead of none update mode with dynamic update logic.
- **UseAltEmpowerCursor**: If set, ‘bolt’ cursor will be used for Empower actions, instead of default ankh cursor.
- **BallisticTrackSource**: If set, when spawned through projectile hit, source attacking unit will be tracked within internal unit data, which can be used for sending underAttack events from auras.
- **DisplayBuildingProgress**: If set, a progress bar will be displayed with construction progress.
- **DisplayUpgradeProgress**: If set, a progress bar will be displayed with upgrade progress.
- **SplitQueue**: If set, when queued, training progress for this unit will only be blocked by units with this flag set and vice-versa.
- **InvulnerableOnBirth**: If set, unit will be invulnerable during Birth sequence/animation.
- **LifespanBirth**: If set, lifespan logic will only be handled as soon as Birth sequence/animation is complete or if it has been skipped.
- **DependentKeepAlive**: If set, unit, when assigned as a dependent unit to another unit, will be kept alive after the source unit dies.
- **AbilitiesOnBothPages**: If set, for units that support economic/military page toggling, castable abilities will be displayed over both pages.
- **ForceSquareSelection**: If set, square selection decal will be rendered for this unit, even if it’s not a building.
- **AutoCommandSingleUnit**: If set, only one instance of the protoUnit will be allowed to have auto-casting for a core ability set as an `ActionCommand` enabled at a time.
- **IgnoreForFavoriteUnitCount**: If set, current protoUnit won't be accounted for favorite unit calculation for postgame stats.
- **PreventRespawn**: If set, `NatureRespawn` and `SelfRespawn` flags will be ignored for this proto unit.
- **PassiveAttack**: If set, attack actions for this unit will only be active when the unit is in combat.
- **ImmuneDamageBonus**: If set, unit won't be affected by the damage bonuses from the damage inflicted by another unit.
- **NoRandomVariation**: If set, unit won’t have randomized variation upon placement.
- **NoBallistic**: If set, ballistic logic will not be used/applied for this unit when assigned as a protoAction projectile.
- **HitSpawnIgnorePlacement**: If set, placement logic will be fully ignored by any Spawn OnHitEffects this unit may have.
- **SpawnWhenHitGround**: Allows HitGround and HitWater spawn events to be handled for flying units when hitting land upon death.
- **AllowAbilityWhenKnockedOut**: Unused and deprecated.
- **AmphibiousBirth**: Allows unit to have separate birth animations for spawning at land and at water.
- **AlwaysShowAbilities**: Unused and deprecated.
- **TradeAddAllyResources**: If set, allies will be granted a percentage of trade income generated by this unit, upon returning to the Market, as defined by the `ModifyAmount` protoAction attribute within the Trade protoAction. In case unit is set to generate additional resources besides gold, the percentage to be granted to allies will be defined by `ModifyExponent`.
- **UseChargeAfterTransform**: After being transformed through a DelayedTransform action, set its ability as used and needs to wait for the cooldown to be used again.
- **GarrisonIgnoreDiplomacy**: If set, unit will be able to garrison within any valid garrisonable building, regardless of the diplomatic stance towards the owner.
- **UseAltConvertCursor**: If set, generic ‘hand’ cursor will be used for conversion actions.
- **NoAutoDelayedTransform**: If set, unit won’t perform automatic delayed transform, if it supports it and is tasked while being on an immovable state.
- **CastAbilitySelf**: When set, allows auto-casting logic to be handled by applicable actions that do not take an enemy target.
- **AutoAbilityStartDisabled**: When set, auto-casting state for abilities that use the default charged time/slot will start as disabled.
- **ForceDeletable**: When set, unit will be deletable even if it’s not commandable.
- **IgnoreDependentEditor**: If set, unit will not spawn dependent units while on Editor.
- **DependentAttach**: If set, instantiates an attaching action when assigned as a dependent unit.
- **AuxAutoAbilityStartDisabled**: When set, auto-casting state for abilities that use the auxiliary charged time/slot will start as disabled.
- **IgnoreIfCharmed**: If set, unit won't be spawned from a source unit that has been generated from Charmed Conversion.
- **IgnoreCarryCapacityWhenDead**: Self-explanatory.
- **ForceCountAsGatherer**: Can be counted as idle?
- **LikeBonusPreview**: Can see the range of other units having the same `LikeBonus` protoaction?
- **TrainVisualUpdate**: Updates the model when training a unit?
- **RespawnTrainOnDeath**: Enables the unit to be respawned through another protoUnit `RespawnTrainData` attribute (set by default for MythUnit by `ShinbokuRespawnEnable` tech, activated when owning a `Goshinboku` tree).
- **UseChargeIfNotIdle**: Reset the cooldown of the charged ability to full if the unit is not idle.
- **UseChargeOnDamaged**: Reset the cooldown of the charged ability to full if the unit is damaged.
- **CanAutoTransform**: For units with a `DelayedTransform` protoaction, will try to perform transformation as soon as possible. It needs to remain idle to transform. The cooldown of the transformation is defined by the recharge time of the protoUnit.
- **CopyNearbyTree**: When set, will hide by copying nearby tree. Can only work when a unit transforms into a protoUnit with this flag.
- **FakeAsTreeIfEnemy**: Acts as a tree for the enemy.
- **NotObscuredByUnitsForOpponents**: Self-explanatory.
- **HideHPBarFromOpponents**: Hides HP bar of the protoUnit when viewed by an enemy player.
- **HideMinimapIndicatorFromOpponents**: Hides minimap indicator of the protoUnit when viewed by an enemy player.
- **NotPreferredMilitaryCommandingUnit**: Unknown effect.
- **BuildingCanDoWorkOnUnitIfCharged**: Unknown effect. Removing it from Communal Hearth doesn’t seem to change its behavior.
- **HasReflectAttack**: Enables reflect attack for this unit.
- **WitherDoesDamage**: Can be damaged by Wither god power.
- **RequireExactRateMatch**: Unknown effect.
- **CheckCreepOnPlacement**: Check if the creep is suitable before placing the unit.
- **PreferSkeletonAsBonePile**: Replace default skeleton bones by a bone pile.
- **PreferBonePileWithSkull**: Replace default skeleton bones by a bone pile with a skull.
- **BloodIgnoresCollideableRestriction**: Self-explanatory.
- **BloodOverlayDisabled**: Enable/disable blood on damaged unit?
- **MoveableWhenTransformed**: Used by ChanequeIdol. Unknown effect.
- **EnableStealthInCutscenes** / **DisableStealthInCutscenes**: Self-explanatory.
- **GrowOnBirthAnim**: Self-explanatory.
- **ShrinkOnDeathAnim**: Self-explanatory.
- **ReturnResourcesOnConstruction**: `ResourceReturnRate` attribute grants resources on construction instead of the destruction of the unit.
- **StealthOnBirth**: Self-explanatory.
- **HostileNature**: In the case of a Nature unit, makes it a valid target for attack-moving units.
- **EnterContextForSelfFoundation**: Forces unit context bindings to be active while a unit is in its foundation form for the current player. This is used to allow hotkeys for PreQueue on transformations on foundations.
- **GamepadSetGatherPointOverride**: For gamepad, allows buildings that don’t have units to train or garrisoned units to set rally/gather points.
- **NeverSeesStealth**: Prevents the flagged unit from revealing units that are in stealth.
- **PreventSelectAllDuringDevotionMinor**: Disables Select All and Select All On-Screen for flagged units performing a DevotionMinor action.
- **IgnoredByStrayProjectiles**: Causes any unit marked with the flag to ignore projectiles for collisions to ensure only direct attacks count as a hit.
- **UpdateLifespanAsPercentOnDataChange**: If set, while applying a Lifespan Data Effect, the relative change is calculated (as multiplier = new / old), and then this multiplier is applied instead to all already existing units. Prevents lifespan timer reset when a technology affects this timer.

