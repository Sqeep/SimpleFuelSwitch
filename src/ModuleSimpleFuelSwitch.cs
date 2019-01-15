﻿using System;

namespace SimpleFuelSwitch
{
    /// <summary>
    /// This PartModule, when provided, allows the part to switch resource contents in the vehicle editor.
    /// The PartModule config will list one or more RESOURCE_OPTION sections, each of which has syntax
    /// similar to a stock RESOURCE block (i.e. you define name, amount, maxAmount).
    /// </summary>
    public class ModuleSimpleFuelSwitch : PartModule
    {
        // Placeholder for when config doesn't provide an (optional) string value.
        private const string DEFAULT_FLAG = "_default";

        private SwitchableResourceSet availableResources = null;

        /// <summary>
        /// Stores the resources ID of the currently selected set of resources. Optional; if
        /// omitted, will be set to the first defined resource option.
        /// </summary>
        [KSPField(isPersistant = true)]
        public string currentResourcesId = DEFAULT_FLAG;

        /// <summary>
        /// Right-click menu item for switching part resources in the vehicle editor.
        /// </summary>
        [KSPEvent(active = true, guiActiveEditor = true, guiName = "Switch Resources")]
        // note, the "Switch Resources" on the above line is a dummy that will get replaced at runtime,
        // so a user should never see it, which is why I haven't bothered to localize it.
        public void DoSwitchResourcesEvent()
        {
            currentResourcesId = availableResources.NextResourcesId(currentResourcesId);
            Logging.Log("Switched resources on " + part.name + " " + part.persistentId + " to " + availableResources[currentResourcesId].displayName);

            UpdateSelectedResources(true);
            OnResourcesSwitched();
        }

        private BaseEvent SwitchResourcesEvent { get { return Events["DoSwitchResourcesEvent"]; } }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            InitializeAvailableResources();

            // If there's a linked variant, we don't want a PAW button for switching
            // resources, since that'll be done by choosing a variant instead.
            if (availableResources.HasAnyLinkedVariants)
            {
                SwitchResourcesEvent.guiActiveEditor = false;
            }
        }

        /// <summary>
        /// Here when a part is initially spawned by clicking on its button on the parts
        /// panel in the editor. Not called when created otherwise, e.g. by alt-clicking
        /// to copy a part, or being instantiated as a symmetry counterpart.
        /// </summary>
        internal void OnPartCreated()
        {
            UpdateSelectedResources(false);
        }

        /// <summary>
        /// Here when any part is attached in the editor. Only called for the actually attached
        /// part, not for any child parts or symmetry counterparts.
        /// </summary>
        /// <param name="part"></param>
        internal static void OnPartAttached(Part part)
        {
            if (OnTreeAttached(part))
            {
                for (int i = 0; i < part.symmetryCounterparts.Count; ++i)
                {
                    OnTreeAttached(part.symmetryCounterparts[i]);
                }
            }
        }

        /// <summary>
        /// Here when a ship loads in the editor or rolling out to the launchpad.
        /// </summary>
        /// <param name="ship"></param>
        internal static void OnShipLoaded(ShipConstruct ship)
        {
            for (int i = 0; i < ship.parts.Count; ++i)
            {
                Part part = ship.parts[i];
                ModuleSimpleFuelSwitch module = TryFind(part);
                if (module != null) module.OnEditorLoad();
            }
        }

        /// <summary>
        /// Here when the part we're on was loaded on a ship in the editor.
        /// </summary>
        private void OnEditorLoad()
        {
            // This is needed because in certain circumstances (it's complicated, depends on the
            // precise sequence of building, saving, loading, launching, etc.), then when the ship
            // is loaded, it adds all the "missing" default resources even though the saved ship
            // doesn't actually contain them. So we need to do a scan through all the parts on the
            // ship, and for any that have ModuleSimpleFuelSwitch on them, check the resources
            // and remove any that don't belong based on the currently selected resource option.

            // To reproduce the case that needs this:
            // 1. create a ship in the editor
            // 2. switch some part so that some of the default resources aren't there anymore
            //    (e.g. a normally LFO part switches to have, say, LF-only)
            // 3. save the ship
            // 4. new
            // 5. load the ship.  Bingo, it has the extra resources in it.
            // 6. launch the ship. Bingo, it has the extra resources in it.

            // This function finds and strips out those unwanted "extra" resources.

            InitializeAvailableResources();
            SwitchableResourceSet.Selection selection = availableResources[currentResourcesId];
            if (selection == null) return;

            bool isDirty = false;
            for (int i = 0; i < part.Resources.Count; ++i)
            {
                // make sure it's supposed to be here...
                PartResource resource = part.Resources[i];
                if (selection.TryFind(resource.resourceName) == null)
                {
                    part.Resources.Remove(resource);
                    isDirty = true;
                }
            }
            if (isDirty)
            {
                part.SimulationResources.Clear();
                part.ResetSimulation();
            }
        }

        /// <summary>
        /// Recursively process every part in a tree, when it's attached. (This gets called
        /// on symmetry counterparts as well.) Returns true if the tree contains a
        /// ModuleSimpleFuelSwitch anywhere, false otherwise.
        /// </summary>
        /// <param name="part"></param>
        /// <returns></returns>
        private static bool OnTreeAttached(Part part)
        {
            bool result = SanitizeResources(part);
            if (part.children == null) return result;
            for (int i = 0; i < part.children.Count; ++i)
            {
                result |= OnTreeAttached(part.children[i]);
            }
            return result;
        }

        /// <summary>
        /// Here when a variant is applied to the part that this module is on.
        /// </summary>
        /// <param name="variant"></param>
        internal void OnVariantApplied(PartVariant variant)
        {
            InitializeAvailableResources();

            // Does this variant have any linked resource selection?
            SwitchableResourceSet.Selection selection = availableResources.TryFindLinkedVariant(variant.Name);
            if (selection == null) return; // nope, nobody cares

            // Found one.  Is that one already selected?
            if (selection.resourcesId == currentResourcesId) return; // Already selected, so nothing to do.

            // Okay, we need to switch to the new selection.
            currentResourcesId = selection.resourcesId;
            Logging.Log(
                "Changed variant on " + part.name + " " + part.persistentId + " to " + variant.Name
                + ", switching resources to " + availableResources[currentResourcesId].displayName);

            UpdateSelectedResources(true);
            OnResourcesSwitched();
        }

        /// <summary>
        /// Here when resources have been switched (either from clicking the button, or picking
        /// a linked variant).
        /// </summary>
        private void OnResourcesSwitched()
        {
            // Setting the part resources munges the PAW but doesn't flag it as
            // dirty, so we need to do that ourselves so that it'll redraw correctly
            // with the revised set of resources available.
            UIPartActionWindow window = UIPartActionController.Instance.GetItem(part);
            if (window != null) window.displayDirty = true;

            // We also need to fire off the "on ship modified" event, so that the engineer
            // report and any other relevant pieces of KSP UI will update as needed.
            if (EditorLogic.fetch != null)
            {
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }
        }

        /// <summary>
        /// Here when we need to set up the resources on a part (i.e. when first initializing, or
        /// when the player changes it by clicking the button).
        /// </summary>
        /// <param name="affectSymCounterparts"></param>
        private void UpdateSelectedResources(bool affectSymCounterparts)
        {
            InitializeAvailableResources();

            if (currentResourcesId == DEFAULT_FLAG)
            {
                currentResourcesId = availableResources.DefaultResourcesId;
            }
            SwitchableResourceSet.Selection selection = availableResources[currentResourcesId];

            // Set the name displayed in the PAW.
            SwitchResourcesEvent.guiName = string.Format(
                "{0}: {1}",
                availableResources.selectorFieldName,
                selection.displayName);

            // Actually set up the resources on the part.
            part.Resources.Clear();
            part.SimulationResources.Clear();
            for (int i = 0; i < selection.resources.Length; ++i)
            {
                part.Resources.Add(selection.resources[i].CreateResourceNode());
            }
            part.ResetSimulation();

            // Also adjust any symmetry counterparts.
            if (affectSymCounterparts)
            {
                for (int i = 0; i < part.symmetryCounterparts.Count; ++i)
                {
                    Part symCounterpart = part.symmetryCounterparts[i];
                    ModuleSimpleFuelSwitch switchModule = TryFind(symCounterpart);
                    if (switchModule != null)
                    {
                        switchModule.currentResourcesId = currentResourcesId;
                        switchModule.UpdateSelectedResources(false);
                    }
                }
            }
        }

        internal void InitializeAvailableResources()
        {
            if (availableResources == null)
            {
                availableResources = SwitchableResourceSet.ForPart(part.name);
            }
        }

        /// <summary>
        /// Try to find the first ModuleSimpleFuelSwitch on the part, or null if not found.
        /// </summary>
        /// <param name="part"></param>
        /// <returns></returns>
        internal static ModuleSimpleFuelSwitch TryFind(Part part)
        {
            if (part == null) return null;
            for (int i = 0; i < part.Modules.Count; ++i)
            {
                ModuleSimpleFuelSwitch module = part.Modules[i] as ModuleSimpleFuelSwitch;
                if (module != null) return module;
            }
            return null; // not found
        }

        /// <summary>
        /// If the part has a ModuleSimpleFuelSwitch, sanitize the resources and return true.
        /// Otherwise, do nothing and return false.
        /// </summary>
        /// <param name="part"></param>
        /// <returns></returns>
        private static bool SanitizeResources(Part part)
        {
            if (TryFind(part) == null) return false;
            // KSP 1.6 has a bug in Part.OnCopy that causes PartResourceList to get
            // corrupted with mismatched dictionary keys. To work around this, we have
            // to clear out and re-insert all the resources.
            //
            // Hopefully a future KSP update will fix the bug and this hack can be removed.
            ConfigNode[] nodes = new ConfigNode[part.Resources.Count];
            for (int i = 0; i < nodes.Length; ++i)
            {
                PartResource resource = part.Resources[i];
                nodes[i] = SwitchableResource.CreateResourceNode(
                    resource.resourceName,
                    resource.amount,
                    resource.maxAmount);
                nodes[i].AddValue("flowState", resource.flowState);
            }
            part.Resources.Clear();
            part.SimulationResources.Clear();
            for (int i = 0; i < nodes.Length; ++i)
            {
                part.Resources.Add(nodes[i]);
            }
            part.ResetSimulation();
            return true;
        }
    }
}
