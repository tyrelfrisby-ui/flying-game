# Existing tests broken by the world

No test source was edited. These are observed failures after the world change, not claims that every failure is a stale assertion. Claude owns reconciliation of tests, spawn/arena/nav/event bindings and final integration.

Baseline: 765 passed, 15 failed, 3 skipped. World: 670 passed, 110 failed, 3 skipped. 97 failing cases are absent from baseline; 13 also fail in baseline.

The baseline has two temporary CREDITS fixture artefacts. LAN failures reflect sandbox socket restrictions. Known aircraft handling/flat-spin and GreatLakes mirror failures are not changed here.

The continuity repair removes the accidental airpark/lake pad-boundary step. The legacy full sweep still reports the deliberately vertical, approved Drake north cliff; its old allow-list has no Drake location. See island-continuity-check.json and the captured continuity output.

## New failures by class

| Class | Cases |
| --- | --- |
| FlightTests.ForestTests | 3 |
| FlightTests.PlateauCopyTests | 5 |
| FlyingGame.FlightTests.AirframeContactTests | 1 |
| FlyingGame.FlightTests.AlaskaTests | 10 |
| FlyingGame.FlightTests.AlpineRangeTests | 7 |
| FlyingGame.FlightTests.ApproachSpawnTests | 2 |
| FlyingGame.FlightTests.BrsTests | 2 |
| FlyingGame.FlightTests.CarrierTests | 1 |
| FlyingGame.FlightTests.ChartRasterTests | 2 |
| FlyingGame.FlightTests.EventTests | 1 |
| FlyingGame.FlightTests.FrankChurchTests | 4 |
| FlyingGame.FlightTests.GroundSurfaceTests | 2 |
| FlyingGame.FlightTests.HighDesertTests | 5 |
| FlyingGame.FlightTests.MazeTowTests | 3 |
| FlyingGame.FlightTests.MountainFrontTests | 1 |
| FlyingGame.FlightTests.NavProcedureTests | 8 |
| FlyingGame.FlightTests.NavigationTests | 5 |
| FlyingGame.FlightTests.PlaygroundTests | 2 |
| FlyingGame.FlightTests.RegionalAtmosphereTests | 9 |
| FlyingGame.FlightTests.SeasideTests | 5 |
| FlyingGame.FlightTests.SlopeAndThermalTests | 1 |
| FlyingGame.FlightTests.VintageNavTests | 3 |
| FlyingGame.FlightTests.WaterfallTests | 3 |
| FlyingGame.FlightTests.WaterfallTunnelTests | 6 |
| FlyingGame.FlightTests.WheelSkiTests | 2 |
| FlyingGame.FlightTests.WorldContinuityTests | 1 |
| FlyingGame.FlightTests.WorldTerrainTests | 3 |

## Each failure absent from baseline

- `FlyingGame.FlightTests.WaterfallTests.RiverDropsCleanlyAtTheLip`: Assert.False() Failure Expected: False Actual:   True
- `FlyingGame.FlightTests.WaterfallTests.GroundUnderTheShelfIsTheLowerPlateau`: fall 0: ground under the shelf at 373 m, lower plateau 305
- `FlyingGame.FlightTests.WaterfallTests.FlyableSlotBehindTheCurtainIsAtLeast150FtDeep`: fall 0: slot floor 372
- `FlyingGame.FlightTests.WheelSkiTests.ACubOnSkisCanBePutOnTheKnikAndStop`: Assert.Equal() Failure: Values differ Expected: Snow Actual:   Rough
- `FlyingGame.FlightTests.WheelSkiTests.TheSameCubOnWheelsSlidesMuchFurtherUpTheKnik`: the ski should slide further even uphill (ski 60 m, wheel 60 m)
- `FlyingGame.FlightTests.PlaygroundTests.MallIs3000FeetOfLandableWaterAndGrass`: Assert.Equal() Failure: Values are not within 3 decimal places Expected: 304.5 (rounded from 304.5) Actual:   2249.424 (rounded from 2249.424)
- `FlyingGame.FlightTests.PlaygroundTests.CanyonLakeIsLongDeepAndWalled`: lake only 0 m long
- `FlyingGame.FlightTests.WaterfallTunnelTests.NoWaterInsideTheTunnelThoughTheRiverRunsOverIt`: Assert.NotNull() Failure: Value of type 'Nullable<double>' does not have a value
- `FlyingGame.FlightTests.WaterfallTunnelTests.TheAmphitheaterBehindEveryCurtainIsDryUnderTheShelf`: Assert.NotNull() Failure: Value of type 'Nullable<double>' does not have a value
- `FlyingGame.FlightTests.WaterfallTunnelTests.ThereIsPavementToLandOnAndItIsPavedOnlyWhereItShouldBe`: Assert.Equal() Failure: Values differ Expected: Paved Actual:   Rough
- `FlyingGame.FlightTests.WaterfallTunnelTests.TheRiverItselfStillHasWaterAboveAndBelowTheFall`: Assert.NotNull() Failure: Value of type 'Nullable<double>' does not have a value
- `FlyingGame.FlightTests.WaterfallTunnelTests.TheBoreFloorIsFlatAndFlushWithTheShelfOutsideItsMouth`: Assert.Equal() Failure: Values are not within 3 decimal places Expected: 304.80000000000001 (rounded from 304.80000000000001) Actual:   370.92399999999998 (rounded from 370.92404408974653)
- `FlyingGame.FlightTests.WaterfallTunnelTests.EveryLegHasAMountainOverItsCrown`: exit.bore at s=0.00: only -11 m of rock over the crown
- `FlyingGame.FlightTests.EventTests.AeroBoxIsOnFlatValleyGround`: Assert.InRange() Failure: Value not in range Range:  (1482.4556828187979 - 1492.4556828187979) Actual: 2745.4452947524405
- `FlyingGame.FlightTests.FrankChurchTests.AOneWayStripHasSomethingAtTheFarEndAndAirAtTheNear`: Soldier Bar: the far end is open — it is not one-way at all (-6 m)
- `FlyingGame.FlightTests.FrankChurchTests.TheBackcountryLeavesTheRestOfTheWorldAlone`: Assert.Equal() Failure: Values are not within 0 decimal places Expected: 2134 (rounded from 2133.6000000000004) Actual:   -30 (rounded from -30)
- `FlyingGame.FlightTests.FrankChurchTests.TheCanyonIsAKilometreDeepAndHasARiverInIt`: at x=-5200 the canyon is only 34 m deep
- `FlyingGame.FlightTests.FrankChurchTests.EveryStripIsWithinTenMinutesOfEveryOther`: Assert.Equal() Failure: Values differ Expected: 10 Actual:   9
- `FlyingGame.FlightTests.AlaskaTests.TheIceIsSnowUnderTheWheelsAndTheRockIsNot`: Assert.Equal() Failure: Values differ Expected: Snow Actual:   Rough
- `FlyingGame.FlightTests.AlaskaTests.ThereIsADenaliAndItIsHuge`: The High One is only 2975 ft
- `FlyingGame.FlightTests.AlaskaTests.BothMadeStripsAreGradedFlatAndClearOfObstacles`: Assert.Equal() Failure: Values are not within 1 decimal place Expected: 227 (rounded from 226.95217524868144) Actual:   226.90000000000001 (rounded from 226.94019853476402)
- `FlyingGame.FlightTests.AlaskaTests.TheGlacierWindAlwaysBlowsDownTheValley`: Long Bar has almost no glacier wind (1.7 m/s)
- `FlyingGame.FlightTests.AlaskaTests.ThePlainOwnsItsOwnGround`: something else owns the ground at (3700,-4050): the plain says 969.9, the world says 906.8
- `FlyingGame.FlightTests.AlaskaTests.TheLanesAreActuallyFlatEnoughToLandOn`: Long Bar's lane falls at 95.3 %
- `FlyingGame.FlightTests.AlaskaTests.EachGlacierEndsInALakeWithBergsFloatingInIt`: Knik has no lake at its foot
- `FlyingGame.FlightTests.AlaskaTests.ThePracticeStripIsTheSizeTheOwnerAskedForAndIsMarked`: Assert.Equal() Failure: Values differ Expected: Gravel Actual:   Rough
- `FlyingGame.FlightTests.AlaskaTests.TheChannelsAreShallowEnoughToSkimAndTheBarsAreDry`: Assert.InRange() Failure: Value not in range Range:  (0.050000000000000003 - 1.3) Actual: 61.083879958593002
- `FlyingGame.FlightTests.AlaskaTests.TheStepsOffTheBarsAreCappedSoTheyCanBeSurvived`: Cut Bank Bar is supposed to be a cut bank, but its step is 0.6 m
- `FlightTests.ForestTests.NobodysRunwayApronOrStripHasTreesOnIt`: timber on PHDH's paved-xwind strip at 6371,5028
- `FlightTests.ForestTests.NothingGrowsInTheWaterOnAGlacierOrInTheDryCountry`: Assert.Equal() Failure: Values differ Expected: 0 Actual:   0.63987689053109809
- `FlightTests.ForestTests.ATreeStandsOnTheGroundAndTheOnesNearTheLineAreStunted`: the line is a wall — nothing is stunted approaching it
- `FlightTests.PlateauCopyTests.WhatEachPlateauDoesCarrySitsOnFlatGroundAtItsOwnLevel`: Assert.InRange() Failure: Value not in range Range:  (3.2671999999999999 - 34.267200000000003) Actual: 1439.8297569861281
- `FlightTests.PlateauCopyTests.SolidsAndAeroBoxAreRegisteredOnEveryPlateau`: Assert.Equal() Failure: Values differ Expected: True Actual:   False
- `FlightTests.PlateauCopyTests.TheValleyCityIsTheCombatCityOnFlatDryGroundClearOfTheOtherFeatures`: Assert.InRange() Failure: Value not in range Range:  (3.2671999999999999 - 5.2671999999999999) Actual: 1390.3217556784982
- `FlightTests.PlateauCopyTests.EachUpperPlateauKeepsTwoThingsAndNothingIsLostAltogether`: Assert.Equal() Failure: Values differ Expected: 2 Actual:   0
- `FlightTests.PlateauCopyTests.StepsAre2000Feet`: Assert.InRange() Failure: Value not in range Range:  (-1 - 1) Actual: -986.000031552
- `FlyingGame.FlightTests.AirframeContactTests.GentleFlightAndNormalLandingsBreakNothing`: Function does not accept floating point Not-a-Number values.
- `FlyingGame.FlightTests.ApproachSpawnTests.HandsOffFromFinalRidesTheGlidePathToTheAimPoint(id: "pa18-cub-like")`: Function does not accept floating point Not-a-Number values.
- `FlyingGame.FlightTests.ApproachSpawnTests.HandsOffFromFinalRidesTheGlidePathToTheAimPoint(id: "glider-2-33-like")`: Function does not accept floating point Not-a-Number values.
- `FlyingGame.FlightTests.CarrierTests.TheDeckIsWhereTheShipIsAndMovesWithIt`: Assert.Equal() Failure: Values are not within 6 decimal places Expected: 36.854866999999999 (rounded from 36.854866776461627) Actual:   5.4389399999999997 (rounded from 5.438940240563694)
- `FlyingGame.FlightTests.ChartRasterTests.TheHighPeaksReadHigherThanTheCoast`: the chart misses the high country: highest 2749 m
- `FlyingGame.FlightTests.ChartRasterTests.TheSeaIsBlueOnEveryLayerThatDrawsGround`: Vfr draws the sea with more red than blue
- `FlyingGame.FlightTests.GroundSurfaceTests.GrassStripSwoopsSmoothlyAndRoughGroundIsBumpy`: Assert.InRange() Failure: Value not in range Range:  (0.29999999999999999 - 0.59999999999999998) Actual: 654.18864696078026
- `FlyingGame.FlightTests.GroundSurfaceTests.RolloutOnRoughGroundIsShortAndViolent`: paved rollout 25 m
- `FlyingGame.FlightTests.HighDesertTests.TheBlockOwnsItsOwnGround`: the ground at -6900,-7450 is -30 m — outside anything the desert builds
- `FlyingGame.FlightTests.HighDesertTests.TheBlockSitsOnTheMesaPlateauAndNowhereNearAnythingElse`: Assert.Equal() Failure: Values differ Expected: Paved Actual:   Rough
- `FlyingGame.FlightTests.HighDesertTests.TheMadeStripsAreGradedAndEmpty`: Red Rock Strip is 997.91 m off its graded plane — it is supposed to have been bladed
- `FlyingGame.FlightTests.HighDesertTests.NoHillCarvesAnother`: Red Rock Mesa: the top is 1002.9 m off its own plane at -5664,-5383 — something carved it
- `FlyingGame.FlightTests.HighDesertTests.ThePlayaIsDeadLevelAndCompletelyClear`: the playa varies by 765.5 m — a dry lake is level
- `FlyingGame.FlightTests.MountainFrontTests.TheAerodromesSitInBaysInTheRange`: Assert.Equal() Failure: Values are not within 1 decimal place Expected: 183 (rounded from 183) Actual:   192.09999999999999 (rounded from 192.0652751622705)
- `FlyingGame.FlightTests.NavigationTests.TheBeaconsSitOnTheGroundTheTableSaysTheyDo`: Assert.Equal() Failure: Values are not within 1 decimal place Expected: 304.80000000000001 (rounded from 304.80000000000001) Actual:   1353.2 (rounded from 1353.1914962803937)
- `FlyingGame.FlightTests.NavigationTests.TheFresnelFadeIsSoftRatherThanABinary`: the fade should be a ramp, not a switch: only 0 partial values
- `FlyingGame.FlightTests.NavigationTests.TheHillSitesBeatTheAirportSites`: the Rimrock crest (9.8 %) should hear more than twice what the Mesa field does (27.1 %)
- `FlyingGame.FlightTests.NavigationTests.TheTerrainBendsTheNeedleButOnlyWhereThereIsTerrain`: the needle lies by 8.0 deg where the ground is flat
- `FlyingGame.FlightTests.NavigationTests.TheGroundInTheWayTakesTheSignalAway`: climbing must help
- `FlyingGame.FlightTests.NavProcedureTests.EveryAirwayLegHasAMinimumAltitudeYouCouldActuallyClimbTo`: SEALS-VLY: MEA 24000 ft is above anything in this fleet
- `FlyingGame.FlightTests.NavProcedureTests.OneApproachHasACourseReversalAndItIsFlownWhereThereIsRoomForIt`: Valley ILS RWY 27: the procedure turn is charted at 3100 ft but 1,000 ft over the highest thing within 2.0 NM is 6100 ft
- `FlyingGame.FlightTests.NavProcedureTests.NoApproachClaimsAProcedureTurnTheTerrainWouldNotAllow`: a 2.0 NM procedure turn at Summit now needs only 4000 ft — the terrain has changed and Summit could have a course reversal after all
- `FlyingGame.FlightTests.NavProcedureTests.EveryPublishedMinimumIsStillWhatTheGroundForces`: Valley 27
- `FlyingGame.FlightTests.NavProcedureTests.TheDescentFromEachFinalApproachFixIsFlyable`: Valley 27
- `FlyingGame.FlightTests.NavProcedureTests.EveryFinalApproachFixIsOnItsOwnFinalApproachCourse`: Valley 27
- `FlyingGame.FlightTests.NavProcedureTests.AnIlsIsAlignedWithItsRunwayAndAVorFinalIsCloseEnough`: Valley 27
- `FlyingGame.FlightTests.NavProcedureTests.TheHillStationsAreWhyNoAirwayMeaIsSetByTheRadio`: Assert.Equal() Failure: Strings differ            ↓ (pos 0) Expected: "terrain" Actual:   "signal"            ↑ (pos 0)
- `FlyingGame.FlightTests.RegionalAtmosphereTests.Airport3IsFiveGustingFifteenFromASteadyDirection`: Atmosphere config: unknown airport anchor 'airport:Bench'.
- `FlyingGame.FlightTests.RegionalAtmosphereTests.Airport2WindWandersAllTheWayRoundAtAboutFiveKnots`: Atmosphere config: unknown airport anchor 'airport:Bench'.
- `FlyingGame.FlightTests.RegionalAtmosphereTests.FullRegionalConditionsApplyAwayFromHome`: Atmosphere config: unknown airport anchor 'airport:Bench'.
- `FlyingGame.FlightTests.RegionalAtmosphereTests.StartAirportIsExactlyCalmIsaFromTheSurfaceThroughThePattern`: Atmosphere config: unknown airport anchor 'airport:Bench'.
- `FlyingGame.FlightTests.RegionalAtmosphereTests.SamplingIsCheap`: Atmosphere config: unknown airport anchor 'airport:Bench'.
- `FlyingGame.FlightTests.RegionalAtmosphereTests.NoThermalStandsInTheCalmZone`: Atmosphere config: unknown airport anchor 'airport:Bench'.
- `FlyingGame.FlightTests.RegionalAtmosphereTests.CalmZoneBlendsOutSmoothlyAndMonotonically`: Atmosphere config: unknown airport anchor 'airport:Bench'.
- `FlyingGame.FlightTests.RegionalAtmosphereTests.RealBluffHasLiftInFrontOfIt`: Atmosphere config: unknown airport anchor 'airport:Bench'.
- `FlyingGame.FlightTests.RegionalAtmosphereTests.CalmZoneIsTheDefaultStartAirportsAndCoversThePattern`: Atmosphere config: unknown airport anchor 'airport:Bench'.
- `FlyingGame.FlightTests.SlopeAndThermalTests.WindOntoTheFirstWallRisesOnTheFaceAndSinksInTheLee`: lift on the windward face
- `FlyingGame.FlightTests.VintageNavTests.TheMailRouteIsLitExceptOnTheEscarpments`: only 7 pots on the route
- `FlyingGame.FlightTests.VintageNavTests.TheRangeCourseIsLowerThanTheVictorAirwayOverTheSameGround`: Assert.Equal() Failure: Strings differ            ↓ (pos 0) Expected: "signal" Actual:   "terrain"            ↑ (pos 0)
- `FlyingGame.FlightTests.VintageNavTests.TheParallelBeaconAirwayCostsTwoThousandFeetForFourHundredMetres`: RED 1 should be well above GREEN 1: 11100 vs 11100
- `FlyingGame.FlightTests.WorldContinuityTests.NowhereDoesTheGroundStopDead`: the ground stops dead at (-3875, -4800): 207 m in 0.1 m (and in 2 square kilometre(s) in all)
- `FlyingGame.FlightTests.WorldTerrainTests.LakesAndRiverHaveWater`: Assert.True() Failure Expected: True Actual:   False
- `FlyingGame.FlightTests.WorldTerrainTests.WallsAreSteepAndIrregular`: Assert.InRange() Failure: Value not in range Range:  (559.60000000000002 - 659.60000000000002) Actual: 480.1443691018078
- `FlyingGame.FlightTests.WorldTerrainTests.AirportsSitOnFlatPadsAtTheirElevations`: Assert.Equal() Failure: Values are not within 3 decimal places Expected: 4.2670000000000003 (rounded from 4.2671999999999999) Actual:   386.17099999999999 (rounded from 386.17067192809475)
- `FlyingGame.FlightTests.MazeTowTests.ACentredGliderIsTowedRoundWithoutTouchingTheRock`: the tow hit the rock on NaN (clearance 179769313486232000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000.0 m)
- `FlyingGame.FlightTests.MazeTowTests.TheTugTakesOffInsideTheTunnel`: it hit the rock on NaN
- `FlyingGame.FlightTests.MazeTowTests.TheGliderPilotCanAskForADifferentBranch`: Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')
- `FlyingGame.FlightTests.AlpineRangeTests.ThePeaksAreMountainsAndTheyCarrySnow`: Assert.InRange() Failure: Value not in range Range:  (3017.6000000000004 - 3237.6000000000004) Actual: 298.2456905192804
- `FlyingGame.FlightTests.AlpineRangeTests.NoOutletValleyTakesTheTopOffAMountain`: Thunder has lost 2777 m off the top
- `FlyingGame.FlightTests.AlpineRangeTests.TheRangeStandsOnTheSummitPlateauAndLeavesEverythingElseAlone`: Assert.InRange() Failure: Value not in range Range:  (2125.6000000000004 - 2141.6000000000004) Actual: 3.1151869352861823
- `FlyingGame.FlightTests.AlpineRangeTests.TheBowledLakesAreReallyEnclosed`: Cirque: the rim is only -2794 m above the water at 0° — that is a way in
- `FlyingGame.FlightTests.AlpineRangeTests.TheOpenLakesHaveOneWayInAndItIsFlyable`: Outlet: the far end is open too — it is a valley, not a lake in a basin
- `FlyingGame.FlightTests.AlpineRangeTests.EveryLakeIsWaterYouCanFloatOn`: Outlet: no water at 0.0 along it
- `FlyingGame.FlightTests.AlpineRangeTests.TheExitsAreGradedFromNothingToAWallOfTimber`: Assert.InRange() Failure: Value not in range Range:  (-6 - 6) Actual: -1526.8200000000002
- `FlyingGame.FlightTests.SeasideTests.IslandHasItsRunwayArchAvalonAndHarborOffshore`: Assert.InRange() Failure: Value not in range Range:  (487.80000000000001 - 488.80000000000001) Actual: 611.5748096813893
- `FlyingGame.FlightTests.SeasideTests.GoldenGateSpansTheMouth`: water under the middle of the bridge
- `FlyingGame.FlightTests.SeasideTests.SeaCaveCanBeTaxiedIntoAndHasABeachAndASkylight`: mouth wide enough for a Beaver (15 m span)
- `FlyingGame.FlightTests.SeasideTests.RiverReachesTheSeaAtSeaLevelThroughCliffs`: Assert.InRange() Failure: Value not in range Range:  (301.80000000000001 - 334.80000000000001) Actual: 1375.4938556324371
- `FlyingGame.FlightTests.SeasideTests.CityStandsOnFlatDryGroundInsideTheCombatZone`: Assert.InRange() Failure: Value not in range Range:  (301.80000000000001 - 312.80000000000001) Actual: 1402.0800000000002
- `FlyingGame.FlightTests.BrsTests.DeployedSlowItCostsAFewHundredFeet`: Assert.InRange() Failure: Value not in range Range:  (150 - 800) Actual: -6851.9903514938578
- `FlyingGame.FlightTests.BrsTests.PulledAtCruiseItZoomsBeforeItSinks`: and 25 seconds later it should be well below where it started

## Failures also in baseline

- `FlyingGame.FlightTests.AerobatGroundHandlingTests.ThreePointAndWheelLandingsAreWorkable(file: "pitts-s2b-like.json", wheel: False)`: rolling: off the runway or swung (21.9 m, 161.1°)
- `FlyingGame.FlightTests.AerobatGroundHandlingTests.ThreePointAndWheelLandingsAreWorkable(file: "pitts-s2b-like.json", wheel: True)`: bounced
- `FlyingGame.FlightTests.AerobatGroundHandlingTests.PittsNeedsQuickFeetTheExtraForgivesRelaxedOnes`: pitts-s2b-like not recovered at 12 m/s
- `FlyingGame.FlightTests.AirframeFlowTests.SmokeGoesRoundTheAirframeInTheFlatSpin`: marker from (-3.5000, 1.2000, 1.6000) inside the airframe at t 0.112: (-4.0793, 0.1223, -0.1702)
- `FlightTests.LanMeshTests.APilotTooFarAwayToSeeGetsASlowerRate`: no free port in 48370..48373
- `FlightTests.LanMeshTests.ChangingAeroplaneTellsTheRoom`: no free port in 48390..48393
- `FlightTests.LanMeshTests.TwoPilotsFindEachOtherWithNoServerAtAll`: no free port in 48310..48313
- `FlightTests.LanMeshTests.APayloadComesThroughUntouchedWhateverIsInIt`: no free port in 48320..48323
- `FlightTests.LanMeshTests.ARoomOnlyHearsItself`: no free port in 48350..48353
- `FlightTests.LanMeshTests.APilotWhoVanishesIsNoticedAndOneWhoLeavesGoesAtOnce`: no free port in 48360..48363
- `FlightTests.LanMeshTests.OnePilotFoundIsTheWholeRoomFound`: no free port in 48330..48333
- `FlightTests.LanMeshTests.ItSweepsWhileItIsAloneAndStopsWhenItIsNot`: no free port in 48380..48383
- `FlyingGame.FlightTests.GlideTableTests.EveryAircraftPassesTheNewAirplaneProtocol`: great-lakes-like: StreamingAssets copy missing or different — copy configs/aircraft/great-lakes-like.json into unity/Assets/StreamingAssets/aircraft/
