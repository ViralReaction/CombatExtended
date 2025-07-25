﻿using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace CombatExtended;
/// <summary>
/// Contains a series of LoadoutSlot slots which define what a pawn using this loadout should try to keep in their inventory.
/// </summary>
public class Loadout : IExposable, ILoadReferenceable, IComparable
{
    #region Fields

    public bool canBeDeleted = true;
    public bool defaultLoadout = false; //NOTE: assumed that there is only ever one loadout which is marked default.
    public string label;
    internal int uniqueID;
    public bool dropUndefined = true;
    public bool adHoc = false;
    public int adHocMags = 4;
    public int adHocMass = 0;
    public int adHocBulk = 0;
    private int _parentID = 0;
#nullable enable
    private Loadout? _parent = null;
#nullable disable
    private List<LoadoutSlot> _slots = new List<LoadoutSlot>();

    #endregion Fields

    #region Constructors

    public Loadout()
    {
        // this constructor is also used by the scribe, in which case defaults generated here will get overwritten.

        // create a unique default name.
        label = LoadoutManager.GetUniqueLabel();

        // create a unique ID.
        uniqueID = LoadoutManager.GetUniqueLoadoutID();
    }

    public Loadout(string label)
    {
        this.label = label;

        // create a unique ID.
        uniqueID = LoadoutManager.GetUniqueLoadoutID();
    }

    /// <summary>
    /// CAUTION: This constructor allows setting the uniqueID and goes unchecked for collisions.
    /// </summary>
    /// <param name="label">string label of new Loadout, preferably unique but not required.</param>
    /// <param name="uniqueID">int ID of new Loadout.  This should be unique to avoid bugs.</param>
    public Loadout(string label, int uniqueID)
    {
        this.label = label;
        this.uniqueID = uniqueID;
    }

    /// <summary>
    /// Handles adding any LoadoutGenercDef as LoadoutSlots if they are flagged as basic.
    /// </summary>
    public void AddBasicSlots()
    {
        IEnumerable<LoadoutGenericDef> defs = DefDatabase<LoadoutGenericDef>.AllDefs.Where(d => d.isBasic);
        foreach (LoadoutGenericDef def in defs)
        {
            LoadoutSlot slot = new LoadoutSlot(def);
            AddSlot(slot);
        }
    }

    #endregion Constructors

    #region Properties
    public int parentID
    {
        get
        {
            return _parentID;
        }
        set
        {
            _parent = null;
            _parentID = value;
        }
    }

#nullable enable
    public Loadout? ParentLoadout
    {
        get
        {
            if (_parent == null && _parentID > 0)
            {
                var p = LoadoutManager.GetLoadoutById(parentID);
                if (p is Loadout)
                {
                    if (p != this && (p._parent == null || (!p.Ancestors.Contains(this))))
                    {
                        _parent = p;
                    }
                    else
                    {
                        _parentID = 0;
                    }
                }
            }
            return _parent;
        }
    }

    public List<LoadoutSlot> OwnSlots
    {
        get
        {
            return _slots;
        }
    }

    public IEnumerable<Loadout> Ancestors
    {
        get
        {
            Loadout? parent = ParentLoadout;
            while (parent is Loadout)
            {
                yield return parent;
                parent = parent.ParentLoadout;
            }
        }
    }
#nullable disable

    public IEnumerable<LoadoutSlot> ParentSlots
    {
        get
        {
            foreach (Loadout ancestor in Ancestors)
            {
                foreach (var slot in ancestor.OwnSlots)
                {
                    yield return slot;
                }
            }
        }
    }

    //TODO 1.6: Turn this into an IEnumerable
    public List<LoadoutSlot> Slots
    {
        get
        {
            if (ParentLoadout is Loadout parent)
            {
                return _slots.Concat(ParentSlots).ToList();
            }
            return _slots;
        }
    }

    public float Bulk
    {
        get
        {
            return Slots.Sum(slot => slot.bulk * slot.count);
        }
    }
    public string LabelCap
    {
        get
        {
            return label.CapitalizeFirst();
        }
    }
    public int SlotCount
    {
        get
        {
            return _slots.Count;
        }
    }
    public float Weight
    {
        get
        {
            return Slots.Sum(slot => slot.mass * slot.count);
        }
    }
    public int UniqueID
    {
        get
        {
            return uniqueID;
        }
    }

    #endregion Properties

    #region Methods

    // Returns a copy of this loadout slot with a new unique ID and a label based on the original name.
    // LoadoutSlots need to be copied.
    /// <summary>
    /// Handles copying one Loadout to a new Loadout object.
    /// </summary>
    /// <param name="source">Loadout from which to copy properties/fields from.</param>
    /// <returns>new Loadout with copied properties from 'source'</returns>
    /// <remarks>
    /// uniqueID will be different as required.
    /// label will be different as required, but related to original.
    /// Slots are copied (not the same object) but have the same properties as source.Slots.
    /// </remarks>
    static Loadout Copy(Loadout source)
    {
        Loadout dest = new Loadout(UniqueLabel(source.label));
        dest.defaultLoadout = source.defaultLoadout;
        dest.canBeDeleted = source.canBeDeleted;
        dest.dropUndefined = source.dropUndefined;
        dest.adHoc = source.adHoc;
        dest.adHocMags = source.adHocMags;
        dest.adHocMass = source.adHocMass;
        dest.adHocBulk = source.adHocBulk;
        dest.parentID = source.parentID;
        dest._slots = new List<LoadoutSlot>();
        foreach (LoadoutSlot slot in source.OwnSlots)
        {
            dest.AddSlot(slot.Copy());
        }
        return dest;
    }

    /// <summary>
    /// Translates the label into a unique label, adding an appropriate suffix if necessary
    /// </summary>
    /// <returns>New unique label</returns>
    static string UniqueLabel(string label)
    {
        Regex reNum = new Regex(@"^(.*?)\d+$");
        if (reNum.IsMatch(label))
        {
            label = reNum.Replace(label, @"$1");
        }
        return LoadoutManager.GetUniqueLabel(label);
    }

    /// <summary>
    /// Copies self to a new Loadout.  <see cref="Copy(Loadout)"/>.
    /// </summary>
    /// <returns>new Loadout with copied properties from self.</returns>
    public Loadout Copy()
    {
        return Copy(this);
    }

    /// <summary>
    /// Handles adding a new LoadoutSlot to self.  If self already has the same slot (based on Def) then increment the already present slot.count.
    /// </summary>
    /// <param name="slot">LoadoutSlot to add to this Loadout.</param>
    public void AddSlot(LoadoutSlot slot)
    {
        LoadoutSlot old = _slots.FirstOrDefault(slot.isSameDef);
        if (old != null)
        {
            old.count += slot.count;
        }
        else
        {
            _slots.Add(slot);
        }
    }

    /// <summary>
    /// Factory method to create a Loadout object from a serializable LoadoutConfig loaded from a loadout save file
    /// </summary>
    /// <remarks>
    /// Note that the logic should be different to scribing data to/from a save file since
    /// additional validation related logic must exist and in some cases certain fields should not be serialized
    /// </remarks>
    /// <param name="loadoutConfig">The LoadoutConfig config object.</param>
    /// <returns>A Loadout object</returns>
    public static Loadout FromConfig(LoadoutConfig loadoutConfig, out List<string> unloadableDefNames)
    {
        // Create the new loadout, preventing name clashes if the loadout already exists
        string uniqueLabel = LoadoutManager.IsUniqueLabel(loadoutConfig.label)
                             ? loadoutConfig.label
                             : UniqueLabel(loadoutConfig.label);

        Loadout loadout = new Loadout(uniqueLabel);
        unloadableDefNames = new List<string>();
        loadout.dropUndefined = loadoutConfig.dropUndefined;
        loadout.adHoc = loadoutConfig.adHoc;
        loadout.adHocMass = loadoutConfig.adHocMass;
        loadout.adHocMags = loadoutConfig.adHocMags;
        loadout.adHocBulk = loadoutConfig.adHocBulk;
        loadout.parentID = LoadoutManager.GetLoadoutByLabel(loadoutConfig.parentLabel)?.uniqueID ?? 0;
        // TODO: Display warning when there's a parent specified but we don't find it.

        // Now create each of the slots
        foreach (LoadoutSlotConfig loadoutSlotConfig in loadoutConfig.slots)
        {
            LoadoutSlot loadoutSlot = LoadoutSlot.FromConfig(loadoutSlotConfig);
            // If the LoadoutSlot could not be loaded then continue loading the others as this most likely means
            // that the current game does not have the mod loaded that was used to create the initial loadout.
            if (loadoutSlot == null)
            {
                unloadableDefNames.Add(loadoutSlotConfig.defName);
                continue;
            }
            loadout.AddSlot(LoadoutSlot.FromConfig(loadoutSlotConfig));
        }

        return loadout;
    }

    /// <summary>
    /// Factory method to create a serializable LoadoutConfig object from this Loadout object
    /// </summary>
    /// <remarks>
    /// Note that the logic should be different to scribing data to/from a save file since
    /// additional validation related logic must exist and in some cases certain fields should not be serialized
    /// </remarks>
    /// <returns>A LoadoutConfig object that can be serialized to a loadout config file.</returns>
    public LoadoutConfig ToConfig()
    {
        List<LoadoutSlotConfig> loadoutSlotConfigList = new List<LoadoutSlotConfig>();

        foreach (LoadoutSlot loadoutSlot in _slots)
        {
            loadoutSlotConfigList.Add(loadoutSlot.ToConfig());
        }

        return new LoadoutConfig
        {
            label = label,
            slots = loadoutSlotConfigList.ToArray(),
            dropUndefined = dropUndefined,
            adHoc = adHoc,
            adHocMags = adHocMags,
            adHocBulk = adHocBulk,
            adHocMass = adHocMass,
            parentLabel = ParentLoadout?.label ?? String.Empty
        };
    }

    /// <summary>
    /// Handles the save/load process as part of IExplosable.
    /// </summary>
    public void ExposeData()
    {
        // basic info about this loadout
        Scribe_Values.Look(ref label, "label");
        Scribe_Values.Look(ref uniqueID, "uniqueID");
        Scribe_Values.Look(ref canBeDeleted, "canBeDeleted", true);
        Scribe_Values.Look(ref defaultLoadout, "defaultLoadout", false);
        Scribe_Values.Look(ref dropUndefined, "dropUndefined", true);
        Scribe_Values.Look(ref adHoc, "adHoc", false);
        Scribe_Values.Look(ref _parentID, "parentID", 0);
        Scribe_Values.Look(ref adHocMags, "adHocMags", 3);
        Scribe_Values.Look(ref adHocMass, "adHocMass", 0);
        Scribe_Values.Look(ref adHocBulk, "adHocBulk", 0);
        _parent = null;
        // slots
        Scribe_Collections.Look(ref _slots, "slots", LookMode.Deep);
    }

    public string GetUniqueLoadID()
    {
        return "Loadout_" + label + "_" + uniqueID;
    }

    /// <summary>
    /// Used to move a slot in this Loadout to a different position in the List.
    /// </summary>
    /// <param name="slot">LoadoutSlot being moved.</param>
    /// <param name="toIndex">int position (index) to move slot to.</param>
    public void MoveSlot(LoadoutSlot slot, int toIndex)
    {
        int fromIndex = _slots.IndexOf(slot);
        MoveTo(fromIndex, toIndex);
    }

    /// <summary>
    /// Used to remove a LoadoutSlot from this Loadout.
    /// </summary>
    /// <param name="slot">LoadoutSlot to remove.</param>
    public void RemoveSlot(LoadoutSlot slot)
    {
        _slots.Remove(slot);
    }

    /// <summary>
    /// Used to remove a LoadoutSlot by index from this Loadout.  Usually used when moving slots around (ie drag and drop).
    /// </summary>
    /// <param name="index">int index of this Loadout's Slot List to remove.</param>
    public void RemoveSlot(int index)
    {
        _slots.RemoveAt(index);
    }

    /// <summary>
    /// Used to move one LoadoutSlot into a different position in this Loadout's List.  Generally connected to drag and drop activities by user.
    /// </summary>
    /// <param name="fromIndex">int index (source) in List to move from.</param>
    /// <param name="toIndex">int index (target) in List to move to.</param>
    /// <returns></returns>
    private int MoveTo(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _slots.Count || toIndex < 0 || toIndex >= _slots.Count)
        {
            throw new Exception("Attempted to move i " + fromIndex + " to " + toIndex + ", bounds are [0," + (_slots.Count - 1) + "].");
        }

        // fetch the filter we're moving
        LoadoutSlot temp = _slots[fromIndex];

        // remove from old location
        _slots.RemoveAt(fromIndex);

        // this may have changed the toIndex
        if (fromIndex + 1 < toIndex)
        {
            toIndex--;
        }

        // insert at new location
        _slots.Insert(toIndex, temp);
        return toIndex;
    }

    /// <summary>
    /// Used to iterate over all slots, including virtual slots, for a specific pawn.
    /// In the trivial case, where this is not an ad-hoc loadout, we can simply yield from our slots
    /// </summary>
    /// <param name="pawn">The pawn to use when generating ad-hoc slots.</param>

    public IEnumerable<LoadoutSlot> GetSlotsFor(Pawn pawn)
    {
        bool weaponInLoadout = true; // assume all needed weapons are already in loadout
        bool ammoInLoadout = true; // assume all needed ammo is already in loadout
        int magSize = 1;
        HashSet<ThingDef> ammoTypes = new HashSet<ThingDef>();
        if (adHoc && ((pawn.Faction?.IsPlayer ?? false) && pawn.equipment?.Primary is Thing primary))
        {
            // ad-hoc, so don't assume the weapon is in the loadout
            CompAmmoUser primaryAmmoUser = primary.TryGetComp<CompAmmoUser>();
            weaponInLoadout = false;
            if (primaryAmmoUser?.UseAmmo ?? false)
            {
                /// We are an ad-hoc loadout, with an ammo-using primary weapon
                /// So figure out what kind of ammo it needs, and check if that ammo is in our slots
                /// if it isn't, provide a virtual slot for it
                ammoInLoadout = false;
                magSize = primaryAmmoUser.Props.magazineSize;

                foreach (AmmoLink link in primaryAmmoUser.Props.ammoSet.ammoTypes)
                {
                    ammoTypes.Add(link.ammo);
                }
            }
        }

        foreach (var slot in Slots)
        {
            yield return slot;
            if (!ammoInLoadout)
            {
                ammoInLoadout = ammoTypes.Contains(slot.thingDef);
            }
            if (!weaponInLoadout)
            {
                weaponInLoadout = pawn.equipment.Primary.def == slot.thingDef;
            }
        }

        if (!weaponInLoadout)
        {
            yield return new LoadoutSlot(pawn.equipment.Primary.def, 1);
        }
        if (!ammoInLoadout)
        {
            // Check if we have ammo in inventory, if so only ask for the same or more of that
            Dictionary<ThingDef, Integer> inventory = pawn.GetStorageByThingDef();
            Dictionary<ThingDef, int> haveAmmo = new Dictionary<ThingDef, int>();
            int totalAmmo = 0;

            foreach (ThingDef def in inventory.Keys)
            {
                if (ammoTypes.Contains(def))
                {
                    haveAmmo[def] = inventory[def].value;
                    totalAmmo += inventory[def].value;
                }
            }
            if (totalAmmo > 0)
            {
                foreach (var ammo in haveAmmo.Keys)
                {
                    int magLimit = adHocMags * magSize;
                    if (adHocMass > 0)
                    {
                        magLimit = Math.Min(magLimit, (int)(adHocMass / ammo.GetStatValueAbstract(StatDefOf.Mass)));
                    }
                    if (adHocBulk > 0)
                    {
                        magLimit = Math.Min(magLimit, (int)(adHocBulk / ammo.GetStatValueAbstract(CE_StatDefOf.Bulk)));
                    }
                    magLimit -= (totalAmmo - haveAmmo[ammo]); // reduce mag limit by total of all other ammo types we already have

                    int magCount = magLimit / magSize;
                    int minMags = (int)(magCount * 0.75f); // works out to 3 out of 4 mags with default settings
                    int minAmmo = minMags * magSize;
                    if (magCount < 2)
                    {
                        minAmmo = magSize;
                    }
                    else if (minMags < 4)
                    {
                        minAmmo = (magCount - 1) * magSize;
                    }
                    if (haveAmmo[ammo] < minAmmo || haveAmmo[ammo] > magLimit)
                    {
                        yield return new LoadoutSlot(ammo, magLimit);
                    }
                    else
                    {
                        yield return new LoadoutSlot(ammo, haveAmmo[ammo]);
                    }
                }
            }
            else
            {
                foreach (var ammo in ammoTypes)
                {
                    int magLimit = adHocMags * magSize;
                    if (adHocMass > 0)
                    {
                        magLimit = Math.Min(magLimit, (int)(adHocMass / ammo.GetStatValueAbstract(StatDefOf.Mass)));
                    }
                    if (adHocBulk > 0)
                    {
                        magLimit = Math.Min(magLimit, (int)(adHocBulk / ammo.GetStatValueAbstract(CE_StatDefOf.Bulk)));
                    }
                    yield return new LoadoutSlot(ammo, magLimit);
                }
            }
        }
    }

    #endregion Methods

    #region IComparable implementation

    /// <summary>
    /// Used when sorting lists of Loadouts.
    /// </summary>
    /// <param name="obj">other object to compare to.</param>
    /// <returns>int -1 indicating this is before obj, 0 indicates this is the same as obj, 1 indicates this is after obj.</returns>
    public int CompareTo(object obj)
    {
        Loadout other = obj as Loadout;
        if (other == null)
        {
            throw new ArgumentException("Loadout.CompareTo(obj), obj is not of type Loadout.");
        }

        // initial case, default comes first.  Currently there aren't more than one default and should never be.
        if (this.defaultLoadout && other.defaultLoadout)
        {
            return 0;
        }
        if (this.defaultLoadout)
        {
            return -1;
        }
        if (other.defaultLoadout)
        {
            return 1;
        }

        // now we just compare by name of loadout...
        return this.label.CompareTo(other.label);
    }

    #endregion
}
