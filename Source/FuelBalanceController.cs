﻿using KSP.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Tac
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class FuelBalanceController : MonoBehaviour
    {
        private Settings settings;
        private MainWindow mainWindow;
        private SettingsWindow settingsWindow;
        private HelpWindow helpWindow;
        private string configFilename;
        private Icon<FuelBalanceController> icon;
        private Dictionary<string, ResourceInfo> resources;
        private Vessel currentVessel;
        private int numberOfParts;
        private Vessel.Situations vesselSituation;

        void Awake()
        {
            Debug.Log("TAC Fuel Balancer [" + this.GetInstanceID().ToString("X") + "][" + Time.time + "]: Awake");
            configFilename = IOUtils.GetFilePathFor(this.GetType(), "FuelBalancer.cfg");

            settings = new Settings();

            settingsWindow = new SettingsWindow(settings);
            helpWindow = new HelpWindow();
            helpWindow.SetSize(500, 200);
            mainWindow = new MainWindow(this, settings, settingsWindow, helpWindow);
            mainWindow.SetSize(300, 200);

            icon = new Icon<FuelBalanceController>(new Rect(Screen.width * 0.8f, 0, 32, 32), "icon.png",
                "Click to show the Fuel Balancer", OnIconClicked);

            resources = new Dictionary<string, ResourceInfo>();
            numberOfParts = 0;
            vesselSituation = Vessel.Situations.PRELAUNCH;
        }

        void Start()
        {
            Debug.Log("TAC Fuel Balancer [" + this.GetInstanceID().ToString("X") + "][" + Time.time + "]: Start");
            Load();

            icon.SetVisible(true);
        }

        void OnDestroy()
        {
            Debug.Log("TAC Fuel Balancer [" + this.GetInstanceID().ToString("X") + "][" + Time.time + "]: OnDestroy");
            icon.SetVisible(false);
            Save();
        }

        void Update()
        {
            foreach (ResourceInfo resourceInfo in resources.Values)
            {
                foreach (ResourcePartMap partInfo in resourceInfo.parts)
                {
                    if (partInfo.isSelected)
                    {
                        partInfo.part.SetHighlightColor(Color.blue);
                        partInfo.part.SetHighlight(true);
                    }
                }
            }
        }

        void FixedUpdate()
        {
            if (!FlightGlobals.fetch)
            {
                Debug.Log("TAC Fuel Balancer [" + this.GetInstanceID().ToString("X") + "][" + Time.time + "]: FlightGlobals are not valid yet.");
                return;
            }

            if (FlightGlobals.fetch.activeVessel == null)
            {
                Debug.Log("TAC Fuel Balancer [" + this.GetInstanceID().ToString("X") + "][" + Time.time + "]: No active vessel yet.");
                return;
            }

            Vessel activeVessel = FlightGlobals.fetch.activeVessel;
            if (activeVessel != currentVessel || activeVessel.Parts.Count != numberOfParts || activeVessel.situation != vesselSituation)
            {
                RebuildLists(activeVessel);
            }

            // Do any fuel transfers
            foreach (ResourceInfo resourceInfo in resources.Values)
            {
                foreach (ResourcePartMap partInfo in resourceInfo.parts)
                {
                    if (partInfo.direction == TransferDirection.IN)
                    {
                        TransferIn(Time.deltaTime, resourceInfo, partInfo);
                    }
                    else if (partInfo.direction == TransferDirection.OUT)
                    {
                        TransferOut(Time.deltaTime, resourceInfo, partInfo);
                    }
                    else if (partInfo.direction == TransferDirection.DUMP)
                    {
                        DumpOut(Time.deltaTime, resourceInfo, partInfo);
                    }
                }

                BalanceResources(Time.deltaTime, resourceInfo.parts.FindAll(rpm => rpm.direction == TransferDirection.BALANCE
                    || (resourceInfo.balance && rpm.direction == TransferDirection.NONE)));
            }
        }

        public Dictionary<string, ResourceInfo> GetResourceInfo()
        {
            return resources;
        }

        public bool IsPrelaunch()
        {
            return currentVessel.situation == Vessel.Situations.PRELAUNCH || currentVessel.situation == Vessel.Situations.LANDED;
        }

        private void Load()
        {
            if (File.Exists<FuelBalanceController>(configFilename))
            {
                ConfigNode config = ConfigNode.Load(configFilename);
                settings.Load(config);
                icon.Load(config);
                mainWindow.Load(config);
                settingsWindow.Load(config);
                helpWindow.Load(config);
            }
        }

        private void Save()
        {
            ConfigNode config = new ConfigNode();
            settings.Save(config);
            icon.Save(config);
            mainWindow.Save(config);
            settingsWindow.Save(config);
            helpWindow.Save(config);

            config.Save(configFilename);
        }

        private void OnIconClicked()
        {
            Debug.Log("TAC Fuel Balancer [" + this.GetInstanceID().ToString("X") + "][" + Time.time + "]: OnIconClicked");
            mainWindow.ToggleVisible();
        }

        private void RebuildLists(Vessel vessel)
        {
            Debug.Log("TAC Fuel Balancer [" + this.GetInstanceID().ToString("X") + "][" + Time.time + "]: Rebuilding resource lists.");

            List<string> toDelete = new List<string>();
            foreach (KeyValuePair<string, ResourceInfo> resourceEntry in resources)
            {
                resourceEntry.Value.parts.RemoveAll(partInfo => !vessel.parts.Contains(partInfo.part));

                if (resourceEntry.Value.parts.Count == 0)
                {
                    toDelete.Add(resourceEntry.Key);
                }
            }

            foreach (string resource in toDelete)
            {
                resources.Remove(resource);
            }

            foreach (Part part in vessel.parts)
            {
                foreach (PartResource resource in part.Resources)
                {
                    if (resources.ContainsKey(resource.resourceName))
                    {
                        List<ResourcePartMap> resourceParts = resources[resource.resourceName].parts;
                        if (!resourceParts.Exists(partInfo => partInfo.part.Equals(part)))
                        {
                            resourceParts.Add(new ResourcePartMap(resource, part));
                        }
                    }
                    else
                    {
                        ResourceInfo resourceInfo = new ResourceInfo();
                        resourceInfo.parts.Add(new ResourcePartMap(resource, part));

                        resources[resource.resourceName] = resourceInfo;
                    }
                }
            }

            numberOfParts = vessel.parts.Count;
            currentVessel = vessel;
            vesselSituation = vessel.situation;
        }

        private void BalanceResources(double deltaTime, List<ResourcePartMap> balanceParts)
        {
            List<PartPercentFull> pairs = new List<PartPercentFull>();
            double totalMaxAmount = 0.0;
            double totalAmount = 0.0;

            foreach (ResourcePartMap partInfo in balanceParts)
            {
                totalMaxAmount += partInfo.resource.maxAmount;
                totalAmount += partInfo.resource.amount;
                double percentFull = partInfo.resource.amount / partInfo.resource.maxAmount;

                pairs.Add(new PartPercentFull(partInfo, percentFull));
            }

            double totalPercentFull = totalAmount / totalMaxAmount;

            // First give to all parts with too little
            double amountLeftToMove = 0.0;
            foreach (PartPercentFull pair in pairs)
            {
                if (pair.percentFull < totalPercentFull)
                {
                    double adjustmentAmount = (pair.partInfo.resource.maxAmount * totalPercentFull) - pair.partInfo.resource.amount;
                    double amountToGive = Math.Min(settings.MaxFuelFlow * settings.RateMultiplier * deltaTime, adjustmentAmount);
                    pair.partInfo.resource.amount += amountToGive;
                    amountLeftToMove += amountToGive;
                }
            }

            // Second take from all parts with too much
            while (amountLeftToMove > 0.000001)
            {
                foreach (PartPercentFull pair in pairs)
                {
                    if (pair.percentFull > totalPercentFull)
                    {
                        double adjustmentAmount = (pair.partInfo.resource.maxAmount * totalPercentFull) - pair.partInfo.resource.amount;
                        double amountToTake = Math.Min(Math.Min(settings.MaxFuelFlow * settings.RateMultiplier * deltaTime / pairs.Count, -adjustmentAmount), amountLeftToMove);
                        pair.partInfo.resource.amount -= amountToTake;
                        amountLeftToMove -= amountToTake;
                    }
                }
            }
        }

        private void TransferIn(double deltaTime, ResourceInfo resourceInfo, ResourcePartMap partInfo)
        {
            var otherParts = resourceInfo.parts.FindAll(rpm => (rpm.resource.amount > 0)
                && (rpm.direction == TransferDirection.NONE || rpm.direction == TransferDirection.OUT || rpm.direction == TransferDirection.DUMP));
            double available = Math.Min(settings.MaxFuelFlow * settings.RateMultiplier * deltaTime, partInfo.resource.maxAmount - partInfo.resource.amount);
            double takeFromEach = available / otherParts.Count;
            double totalTaken = 0.0;

            foreach (ResourcePartMap otherPartInfo in otherParts)
            {
                if (partInfo.part != otherPartInfo.part)
                {
                    double amountTaken = Math.Min(takeFromEach, otherPartInfo.resource.amount);
                    otherPartInfo.resource.amount -= amountTaken;

                    totalTaken += amountTaken;
                }
            }

            partInfo.resource.amount += totalTaken;
        }

        private void TransferOut(double deltaTime, ResourceInfo resourceInfo, ResourcePartMap partInfo)
        {
            var otherParts = resourceInfo.parts.FindAll(rpm => ((rpm.resource.maxAmount - rpm.resource.amount) > 0)
                && (rpm.direction == TransferDirection.NONE || rpm.direction == TransferDirection.IN));
            double available = Math.Min(settings.MaxFuelFlow * settings.RateMultiplier * deltaTime, partInfo.resource.amount);
            double giveToEach = available / otherParts.Count;
            double totalGiven = 0.0;

            foreach (ResourcePartMap otherPartInfo in otherParts)
            {
                if (partInfo.part != otherPartInfo.part)
                {
                    double amountGiven = Math.Min(giveToEach, otherPartInfo.resource.maxAmount - otherPartInfo.resource.amount);
                    otherPartInfo.resource.amount += amountGiven;

                    totalGiven += amountGiven;
                }
            }

            partInfo.resource.amount -= totalGiven;
        }

        private void DumpOut(double deltaTime, ResourceInfo resourceInfo, ResourcePartMap partInfo)
        {
            double available = Math.Min(settings.MaxFuelFlow * settings.RateMultiplier * deltaTime, partInfo.resource.amount);
            partInfo.resource.amount -= available;
        }
    }
}