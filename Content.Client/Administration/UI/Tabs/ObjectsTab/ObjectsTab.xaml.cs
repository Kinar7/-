using Content.Client.Station;
using Content.Client.UserInterface.Controls;
using Robust.Client.AutoGenerated;
using Robust.Client.Console;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client.Administration.UI.Tabs.ObjectsTab;

[GenerateTypedNameReferences]
public sealed partial class ObjectsTab : Control
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IClientConsoleHost _console = default!;

    private readonly List<ObjectsTabEntry> _objects = new();
    private readonly List<ObjectsTabSelection> _selections = new();
    private bool _ascending = false;  // Set to false for descending order by default
    private ObjectsTabHeader.Header _headerClicked = ObjectsTabHeader.Header.ObjectName;
    private readonly Color _altColor = Color.FromHex("#292B38");
    private readonly Color _defaultColor = Color.FromHex("#2F2F3B");

    public event Action<GUIBoundKeyEventArgs, ListData>? OnEntryKeyBindDown;

    private readonly TimeSpan _updateFrequency = TimeSpan.FromSeconds(2);
    private TimeSpan _nextUpdate;

    private Action<NetEntity>? TeleportToObjAction { get; set; }
    private Action<NetEntity>? DeleteObjAction { get; set; }

    public ObjectsTab()
    {
        RobustXamlLoader.Load(this);
        IoCManager.InjectDependencies(this);

        ObjectTypeOptions.OnItemSelected += ev =>
        {
            ObjectTypeOptions.SelectId(ev.Id);
            RefreshObjectList(_selections[ev.Id]);
        };

        foreach (var type in Enum.GetValues(typeof(ObjectsTabSelection)))
        {
            _selections.Add((ObjectsTabSelection)type!);
            ObjectTypeOptions.AddItem(Loc.GetString($"object-tab-object-type-{((Enum.GetName((ObjectsTabSelection)type))!.ToString().ToLower())}"));
        }

        ListHeader.OnHeaderClicked += HeaderClicked;
        SearchList.SearchBar = SearchLineEdit;
        SearchList.GenerateItem += GenerateButton;
        SearchList.DataFilterCondition += DataFilterCondition;

        RefreshObjectList();
        // Set initial selection and refresh the list to apply the initial sort order
        var defaultSelection = ObjectsTabSelection.Grids;
        ObjectTypeOptions.SelectId((int)defaultSelection); // Set the default selection
        RefreshObjectList(defaultSelection); // Refresh the list with the default selection

        // Initialize the next update time
        _nextUpdate = TimeSpan.Zero;

        TeleportToObjAction += TeleportToObj;
        DeleteObjAction += DeleteObj;
    }

    private void TeleportToObj(NetEntity nent)
    {
        _console.ExecuteCommand($"tpto {nent}");
    }

    private void DeleteObj(NetEntity nent)
    {
        _console.ExecuteCommand($"delete {nent}");
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + _updateFrequency;

        RefreshObjectList();
    }

    private void RefreshObjectList()
    {
        RefreshObjectList(_selections[ObjectTypeOptions.SelectedId]);
    }

    private void RefreshObjectList(ObjectsTabSelection selection)
    {
        var entities = new List<(string Name, NetEntity Entity)>();
        switch (selection)
        {
            case ObjectsTabSelection.Stations:
                entities.AddRange(_entityManager.EntitySysManager.GetEntitySystem<StationSystem>().Stations);
                break;
            case ObjectsTabSelection.Grids:
            {
                var query = _entityManager.AllEntityQueryEnumerator<MapGridComponent, MetaDataComponent>();
                while (query.MoveNext(out var uid, out _, out var metadata))
                {
                    entities.Add((metadata.EntityName, _entityManager.GetNetEntity(uid)));
                }

                break;
            }
            case ObjectsTabSelection.Maps:
            {
                var query = _entityManager.AllEntityQueryEnumerator<MapComponent, MetaDataComponent>();
                while (query.MoveNext(out var uid, out _, out var metadata))
                {
                    entities.Add((metadata.EntityName, _entityManager.GetNetEntity(uid)));
                }
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(selection), selection, null);
        }

        entities.Sort((a, b) =>
        {
            var valueA = GetComparableValue(a, _headerClicked);
            var valueB = GetComparableValue(b, _headerClicked);
            return _ascending ? Comparer<object>.Default.Compare(valueA, valueB) : Comparer<object>.Default.Compare(valueB, valueA);
        });

        var listData = new List<ObjectsListData>();
        for (int index = 0; index < entities.Count; index++)
        {
            var info = entities[index];
            listData.Add(new ObjectsListData(info, $"{info.Name} {info.Entity}", index % 2 == 0 ? _altColor : _defaultColor));
        }

        SearchList.PopulateList(listData);
    }

    private void GenerateButton(ListData data, ListContainerButton button)
    {
        if (data is not ObjectsListData { Info: var info, BackgroundColor: var backgroundColor })
            return;

        var entry = new ObjectsTabEntry(info.Name, info.Entity, new StyleBoxFlat { BackgroundColor = backgroundColor }, TeleportToObjAction, DeleteObjAction);
        button.ToolTip = $"{info.Name}, {info.Entity}";

        button.OnKeyBindDown += args => OnEntryKeyBindDown?.Invoke(args, data);
        button.AddChild(entry);
    }

    private bool DataFilterCondition(string filter, ListData listData)
    {
        if (listData is not ObjectsListData { FilteringString: var filteringString })
            return false;

        // If the filter is empty, do not filter out any entries
        if (string.IsNullOrEmpty(filter))
            return true;

        return filteringString.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
    }

    private object GetComparableValue((string Name, NetEntity Entity) entity, ObjectsTabHeader.Header header)
    {
        return header switch
        {
            ObjectsTabHeader.Header.ObjectName => entity.Name,
            ObjectsTabHeader.Header.EntityID => entity.Entity.ToString(),
            _ => entity.Name
        };
    }

    private void HeaderClicked(ObjectsTabHeader.Header header)
    {
        if (_headerClicked == header)
        {
            _ascending = !_ascending;
        }
        else
        {
            _headerClicked = header;
            _ascending = true;
        }

        ListHeader.UpdateHeaderSymbols(_headerClicked, _ascending);
        RefreshObjectList();
    }

    private enum ObjectsTabSelection
    {
        Grids,
        Maps,
        Stations,
    }
}

public record ObjectsListData((string Name, NetEntity Entity) Info, string FilteringString, Color BackgroundColor) : ListData;
