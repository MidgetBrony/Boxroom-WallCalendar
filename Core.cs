using System.Globalization;
using HarmonyLib;
using MelonLoader;
using ModsPanel;
using SteamShelf;
using SteamShelf.Placeables;
using SteamShelf.PlayerTools;
using UnityEngine;

[assembly: MelonInfo(typeof(BoxroomWallCalendar.Core), "Wall Calendar", "1.6.0", "MidgetBrony")]
[assembly: MelonGame("NestedLoop", "BOXROOM")]
namespace BoxroomWallCalendar;

public sealed class Core : MelonMod
{
    internal const string ItemId = "BoxroomWallCalendar_Simple";
    internal static PlaceableData Data; internal static Sprite Icon;
    public override void OnInitializeMelon()
    {
        Data = ScriptableObject.CreateInstance<PlaceableData>(); Data.ID = ItemId; Data.DisplayName = "Wall Calendar";
        Data.IsLoadedFromMod = true; Data.PlacementType = (PlacementType)1; Data.ModScale = 1f; Data.SetToolType(EToolType.Placeable);
        Icon = CalendarRenderer.CreateIcon();
        CalendarTextCanvas.Initialize();
        CalendarDoodles.Initialize();
        CalendarFeeds.Initialize();
        LoggerInstance.Msg("Registered Wall Calendar in BOXROOM's Mods catalogue.");
    }
}

[HarmonyPatch(typeof(ModsPlaceableLoader), nameof(ModsPlaceableLoader.LoadPlaceableData))]
internal static class LoadPatch
{
    private static void Postfix(ref Task<List<PlaceableData>> __result) => __result = Append(__result);
    private static async Task<List<PlaceableData>> Append(Task<List<PlaceableData>> original)
    { var result = await original ?? new List<PlaceableData>(); if (Core.Data != null && !result.Any(x => x != null && x.ID == Core.ItemId)) result.Add(Core.Data); return result; }
}
[HarmonyPatch(typeof(PlaceableManager), nameof(PlaceableManager.InstantiatePlaceableAsync))]
internal static class InstantiatePatch
{
    private static bool Prefix(PlaceableData data, ref Task<GameObject> __result)
    { if (data == null || data.ID != Core.ItemId) return true; __result = Task.FromResult(CalendarFactory.Create(data)); return false; }
}
[HarmonyPatch(typeof(PlaceableManager), nameof(PlaceableManager.GetSpriteAsync))]
internal static class SpritePatch
{
    private static bool Prefix(PlaceableData data, ref Task<Sprite> __result)
    { if (data == null || data.ID != Core.ItemId) return true; __result = Task.FromResult(Core.Icon); return false; }
}

internal sealed class LiveCalendar : MonoBehaviour
{
    private DateTime shownDate; private Renderer face; private Texture2D texture; private bool feedsChanged;
    internal void Initialize(Renderer target) { face = target; Refresh(true); }
    private void Start()
    {
        // Placement clones copy component fields, including the source texture.
        // Give every clone a freshly rendered face before the source is destroyed.
        if (face == null)
        {
            Transform printedFace = transform.Find("Printed calendar face");
            if (printedFace != null) face = printedFace.GetComponent<Renderer>();
        }
        if (face != null) Refresh(true);
    }
    private void OnEnable() { CalendarFeeds.Changed += OnFeedsChanged; }
    private void OnDisable() { CalendarFeeds.Changed -= OnFeedsChanged; }
    private void OnFeedsChanged() { feedsChanged = true; }
    private void Update()
    {
        if (feedsChanged) { feedsChanged = false; Refresh(true); }
        else if (Time.frameCount % 300 == 0) Refresh(false);
        // PlaceablePainter temporarily swaps materials in build mode. If BOXROOM
        // restores a blank/black material afterwards, put the printed face back.
        if (texture != null && face != null && face.sharedMaterial != null &&
            face.sharedMaterial.mainTexture != texture)
        {
            Color color = face.sharedMaterial.color;
            if (color.r < .08f && color.g < .08f && color.b < .08f) AttachTexture();
        }
    }
    private void Refresh(bool force)
    {
        DateTime now = DateTime.Now.Date; if (!force && now == shownDate) return; shownDate = now;
        Texture2D replacement = CalendarRenderer.Render(now, now, 512, 704);
        // Do not destroy the previous texture here. A BOXROOM placement clone may
        // still share it for the remainder of the placement/commit frame.
        texture = replacement; AttachTexture();
    }
    private void AttachTexture()
    {
        Material material = CalendarFactory.CreateFaceMaterial(); material.mainTexture = texture; face.sharedMaterial = material;
    }
    // Intentionally no texture destruction: Unity placement cloning initially
    // shares this reference. Destroying the source texture blackens the placed copy.
}

internal static class CalendarFactory
{
    internal static GameObject Create(PlaceableData data)
    {
        GameObject root = new("Wall Calendar"); int layer = LayerMask.NameToLayer("Prop"); root.layer = layer;
        // GameImagePainter must receive its serialized references before Awake.
        root.SetActive(false);
        var tag = root.AddComponent<PlacementTag>(); tag.PlaceableData = data;
        GameObject backing = Cube(root.transform, "Calendar backing", new Vector3(.55f, .76f, .026f), new Vector3(0, 0, -.013f), new Color(.035f, .13f, .15f));
        UnityEngine.Object.Destroy(backing.GetComponent<Collider>());
        BoxCollider interactionCollider = root.AddComponent<BoxCollider>();
        interactionCollider.size = new Vector3(.55f, .76f, .026f);
        interactionCollider.center = new Vector3(0, 0, -.013f);
        GameObject face = Quad(root.transform, "Printed calendar face", new Vector3(.53f, .735f, 1), new Vector3(0, 0, -.027f));
        Renderer faceRenderer = face.GetComponent<Renderer>(); faceRenderer.material = CreateFaceMaterial();
        GameObject art = Quad(root.transform, "Changeable calendar art", new Vector3(.53f, .266f, 1), new Vector3(0, .2345f, -.029f));
        Renderer artRenderer = art.GetComponent<Renderer>();
        Material artMaterial = CreateFaceMaterial(); artMaterial.mainTexture = CalendarRenderer.RenderArt(512, 255); artRenderer.material = artMaterial;
        Cube(root.transform, "Binding rail", new Vector3(.51f, .012f, .018f), new Vector3(0, .358f, -.040f), new Color(.72f, .75f, .73f));
        for (int i = 0; i < 11; i++) Cube(root.transform, "Binding ring", new Vector3(.009f, .032f, .009f), new Vector3(-.225f + i * .045f, .358f, -.047f), new Color(.78f, .80f, .78f));
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
        // GameImagePainter must be the first IPlaceable on the object. BOXROOM uses
        // TryGetComponent<IPlaceable>, so a PlaceablePainter added first steals the
        // save-state link and makes native custom artwork non-persistent.
        var provider = root.AddComponent<SteamGameDataProvider>();
        var imagePainter = root.AddComponent<GameImagePainter>();
        AccessTools.Field(typeof(GameImagePainter), "targetRenderer").SetValue(imagePainter, artRenderer);
        AccessTools.Field(typeof(GameImagePainter), "materialIndex").SetValue(imagePainter, 0);
        // The header is a landscape 2:1 panel, so use BOXROOM screenshots/extra
        // artwork rather than stretching portrait box or album covers into it.
        AccessTools.Field(typeof(GameImagePainter), "shouldPaintScreenshots").SetValue(imagePainter, true);
        AccessTools.Field(typeof(GameImagePainter), "overrideMaterial").SetValue(imagePainter, null);
        AccessTools.Field(typeof(GameImagePainter), "provider").SetValue(imagePainter, provider);
        var painter = root.AddComponent<PlaceablePainter>(); painter.InitializeMaterials(); painter.SetupAsForcedPlaceable();
        root.AddComponent<LiveCalendar>().Initialize(faceRenderer);
        root.AddComponent<CalendarArtAspect>().Initialize(artRenderer);
        root.AddComponent<CalendarArtVerifier>();
        root.AddComponent<CalendarInteractable>();
        root.SetActive(true); return root;
    }
    private static GameObject Cube(Transform parent, string name, Vector3 scale, Vector3 position, Color color)
    { GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false); go.transform.localScale = scale; go.transform.localPosition = position; go.GetComponent<Renderer>().material = SolidMaterial(color); return go; }
    private static GameObject Quad(Transform parent, string name, Vector3 scale, Vector3 position)
    { GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = name; go.transform.SetParent(parent, false); go.transform.localScale = scale; go.transform.localPosition = position; UnityEngine.Object.Destroy(go.GetComponent<Collider>()); return go; }
    private static Shader FindShader(bool textured) => Shader.Find(textured ? "Unlit/Texture" : "Unlit/Color") ?? Shader.Find("Standard");
    private static Material SolidMaterial(Color color) => new(FindShader(false)) { color = color };
    internal static Material CreateFaceMaterial() => new(FindShader(true)) { color = Color.white };
}

internal sealed class CalendarArtAspect : MonoBehaviour
{
    private const float TargetAspect = .53f / .266f;
    private Renderer target;
    private Texture lastTexture;

    internal void Initialize(Renderer renderer) { target = renderer; Apply(); }
    private void Start()
    {
        if (target == null)
        {
            Transform art = transform.Find("Changeable calendar art");
            if (art != null) target = art.GetComponent<Renderer>();
        }
        Apply();
    }
    private void Update()
    {
        if (target != null && target.sharedMaterial != null && target.sharedMaterial.mainTexture != lastTexture) Apply();
    }
    private void Apply()
    {
        if (target == null || target.sharedMaterial == null) return;
        Texture texture = target.sharedMaterial.mainTexture;
        if (texture == null || texture.width <= 0 || texture.height <= 0) return;
        lastTexture = texture;
        float sourceAspect = (float)texture.width / texture.height;
        Vector2 scale = Vector2.one, offset = Vector2.zero;
        if (sourceAspect > TargetAspect)
        {
            scale.x = TargetAspect / sourceAspect; offset.x = (1f - scale.x) * .5f;
        }
        else if (sourceAspect < TargetAspect)
        {
            scale.y = sourceAspect / TargetAspect; offset.y = (1f - scale.y) * .5f;
        }
        Material material = target.sharedMaterial;
        material.mainTextureScale = scale; material.mainTextureOffset = offset;
        if (material.HasProperty("_BaseMap")) { material.SetTextureScale("_BaseMap", scale); material.SetTextureOffset("_BaseMap", offset); }
        if (material.HasProperty("_MainTex")) { material.SetTextureScale("_MainTex", scale); material.SetTextureOffset("_MainTex", offset); }
    }
}

internal sealed class CalendarArtVerifier : MonoBehaviour
{
    private void Start()
    {
        GameImagePainter painter = GetComponent<GameImagePainter>();
        IPlaceable linked = GetComponent<IPlaceable>();
        MelonLogger.Msg($"[Wall Calendar] Native art ready: painter={painter != null}, firstPlaceable={linked?.GetType().Name}, rootCollider={GetComponent<BoxCollider>() != null}");
    }
}

internal sealed class CalendarInteractable : MonoBehaviour, IInteractable
{
    public void OnInteract() => CalendarViewer.Toggle(this);
    public void OnAlternateInteract() { }
}

internal static class CalendarViewer
{
    private static CalendarInteractable owner;
    private static DateTime displayedMonth;
    private static Texture2D grid;
    private static ModMenu menu;

    internal static void Toggle(CalendarInteractable source)
    {
        if (menu != null && menu.IsOpen && owner == source) { menu.Close(); return; }
        owner = source; DateTime now = DateTime.Now;
        displayedMonth = new DateTime(now.Year, now.Month, 1); Show();
    }

    private static void ChangeMonth(int amount) { displayedMonth = displayedMonth.AddMonths(amount); Show(); }
    private static void ClickCalendar(Vector2 point)
    {
        const int width=700,height=950,left=35,cellWidth=90;
        float x=point.x*width,y=point.y*height;
        if(y<18f||y>410f||x<left||x>=left+cellWidth*7) return;
        int col=Mathf.FloorToInt((x-left)/cellWidth),row=Mathf.FloorToInt((407f-y)/65f);
        if(row<0||row>5) return;
        CultureInfo culture=CultureInfo.CurrentCulture; DayOfWeek firstDay=culture.DateTimeFormat.FirstDayOfWeek;
        DateTime first=new(displayedMonth.Year,displayedMonth.Month,1);
        int start=((int)first.DayOfWeek-(int)firstDay+7)%7;
        int day=row*7+col-start+1;
        if(day<1||day>DateTime.DaysInMonth(displayedMonth.Year,displayedMonth.Month)) return;
        DateTime selected=new(displayedMonth.Year,displayedMonth.Month,day);
        List<CalendarEvent> events=CalendarFeeds.EventsFor(selected);
        if(events.Count==0) return;
        ShowEvents(selected,events);
    }

    private static void ShowEvents(DateTime day,List<CalendarEvent> events)
    {
        ModMenu details=ModsUi.CreateMenu("MidgetBrony.WallCalendar.Events",day.ToString("ddd d MMM yyyy",CultureInfo.CurrentCulture));
        details.Eyebrow="CALENDAR";
        foreach(CalendarEvent item in events) details.AddButton(item.Title,()=>{},CalendarFeeds.Describe(item,day));
        details.AddButton("Back to calendar",Show);
        details.Closed=Cleanup;
        menu=details;details.Show();
    }

    private static void Cleanup()
    {
        owner=null;menu=null;
        if(grid!=null){UnityEngine.Object.Destroy(grid);grid=null;}
    }
    private static Texture GetArt()
    {
        Transform art = owner != null ? owner.transform.Find("Changeable calendar art") : null;
        Renderer renderer = art != null ? art.GetComponent<Renderer>() : null;
        if (renderer == null) return null;
        Material material = renderer.material;
        if (material == null) return null;
        Texture texture = material.mainTexture;
        if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null) texture = material.GetTexture("_BaseMap");
        if (texture == null && material.HasProperty("_MainTex")) texture = material.GetTexture("_MainTex");
        return texture;
    }

    private static void Show()
    {
        if (grid != null) UnityEngine.Object.Destroy(grid);
        grid = CalendarRenderer.ComposeViewer(GetArt(), displayedMonth, DateTime.Now.Date, 700, 950);
        ModMenu next = ModsUi.CreateMenu("MidgetBrony.WallCalendar.Viewer", "WALL CALENDAR");
        next.FocusViewer = true;
        next.AddImage(() => grid, ClickCalendar, 820f)
            .AddButton("<", () => ChangeMonth(-1))
            .AddButton(">", () => ChangeMonth(1));
        next.Closed = () =>
        {
            if (menu != next) return;
            Cleanup();
        };
        menu = next; menu.Show();
    }
}

internal static class CalendarRenderer
{
    private static readonly Color32 Paper = new(246,242,224,255), Ink = new(10,43,49,255), Red = new(224,38,31,255), Sky = new(92,157,174,255), Mountain = new(207,218,211,255), Ridge = new(76,100,101,255), Ground = new(131,79,42,255);
    private static string[] P(params string[] rows) => rows;
    private static readonly Dictionary<char,string[]> Font = new()
    {
        ['A']=P("01110","10001","10001","11111","10001","10001","10001"),['B']=P("11110","10001","10001","11110","10001","10001","11110"),['C']=P("01111","10000","10000","10000","10000","10000","01111"),
        ['D']=P("11110","10001","10001","10001","10001","10001","11110"),['E']=P("11111","10000","10000","11110","10000","10000","11111"),['F']=P("11111","10000","10000","11110","10000","10000","10000"),
        ['G']=P("01111","10000","10000","10111","10001","10001","01111"),['H']=P("10001","10001","10001","11111","10001","10001","10001"),['I']=P("11111","00100","00100","00100","00100","00100","11111"),
        ['J']=P("00111","00010","00010","00010","10010","10010","01100"),['K']=P("10001","10010","10100","11000","10100","10010","10001"),['L']=P("10000","10000","10000","10000","10000","10000","11111"),
        ['M']=P("10001","11011","10101","10101","10001","10001","10001"),['N']=P("10001","11001","10101","10011","10001","10001","10001"),['O']=P("01110","10001","10001","10001","10001","10001","01110"),
        ['P']=P("11110","10001","10001","11110","10000","10000","10000"),['Q']=P("01110","10001","10001","10001","10101","10010","01101"),['R']=P("11110","10001","10001","11110","10100","10010","10001"),
        ['S']=P("01111","10000","10000","01110","00001","00001","11110"),['T']=P("11111","00100","00100","00100","00100","00100","00100"),['U']=P("10001","10001","10001","10001","10001","10001","01110"),
        ['V']=P("10001","10001","10001","10001","10001","01010","00100"),['W']=P("10001","10001","10001","10101","10101","10101","01010"),['X']=P("10001","10001","01010","00100","01010","10001","10001"),
        ['Y']=P("10001","10001","01010","00100","00100","00100","00100"),['Z']=P("11111","00001","00010","00100","01000","10000","11111"),
        ['0']=P("01110","10001","10011","10101","11001","10001","01110"),['1']=P("00100","01100","00100","00100","00100","00100","01110"),['2']=P("01110","10001","00001","00010","00100","01000","11111"),
        ['3']=P("11110","00001","00001","01110","00001","00001","11110"),['4']=P("00010","00110","01010","10010","11111","00010","00010"),['5']=P("11111","10000","10000","11110","00001","00001","11110"),
        ['6']=P("01110","10000","10000","11110","10001","10001","01110"),['7']=P("11111","00001","00010","00100","01000","01000","01000"),['8']=P("01110","10001","10001","01110","10001","10001","01110"),['9']=P("01110","10001","10001","01111","00001","00001","01110")
    };
    internal static Texture2D Render(DateTime displayedMonth, DateTime today, int width, int height)
    {
        Texture2D t = NewTexture(width,height); Fill(t,Paper); Rect(t,0,height-255,width,255,Sky);
        Triangle(t,-20,height-255,200,height-75,330,height-255,Mountain); Triangle(t,130,height-255,340,height-25,550,height-255,Ridge); Triangle(t,300,height-255,445,height-105,570,height-255,Mountain); Rect(t,0,height-270,width,62,Ground);
        CultureInfo culture=CultureInfo.CurrentCulture; string month=culture.DateTimeFormat.GetMonthName(displayedMonth.Month).ToUpper(culture);
        Rect(t,28,height-375,width-56,3,Ink);
        DayOfWeek firstDay=culture.DateTimeFormat.FirstDayOfWeek; string[] shortest=culture.DateTimeFormat.ShortestDayNames; int cw=64,left=31;
        DateTime first=new(displayedMonth.Year,displayedMonth.Month,1); int start=((int)first.DayOfWeek-(int)firstDay+7)%7;
        using(var text=new CalendarTextCanvas(t))
        {
            text.Center(month,width/2-38,height-302,43,Ink,CalendarFontWeight.SemiBold);
            text.Right(displayedMonth.Year.ToString(culture),width-28,height-302,30,Ink,CalendarFontWeight.SemiBold);
            for(int col=0;col<7;col++){DayOfWeek wd=(DayOfWeek)(((int)firstDay+col)%7);string label=shortest[(int)wd].Substring(0,1).ToUpper(culture);text.Center(label,left+col*cw+cw/2,height-397,22,Weekend(wd)?Red:Ink,CalendarFontWeight.Medium);}
            for(int day=1;day<=DateTime.DaysInMonth(displayedMonth.Year,displayedMonth.Month);day++){int slot=start+day-1,col=slot%7,row=slot/7,cx=left+col*cw+cw/2,cy=height-453-row*52;DateTime cell=first.AddDays(day-1);text.Center(day.ToString(culture),cx,cy,25,Weekend(cell.DayOfWeek)?Red:Ink,CalendarFontWeight.Medium);}
            text.Apply();
        }
        for(int day=1;day<=DateTime.DaysInMonth(displayedMonth.Year,displayedMonth.Month);day++){int slot=start+day-1,col=slot%7,row=slot/7,cx=left+col*cw+cw/2,cy=height-464-row*52;DateTime cell=first.AddDays(day-1);CalendarDoodles.DrawDateMark(t,cell,cx,cy+8,34,28);if(cell<today) HandX(t,cx,cy+9,13,Red,cell);}
        t.Apply(false,false); return t;
    }
    internal static Texture2D RenderArt(int width, int height)
    {
        Texture2D t=NewTexture(width,height); Fill(t,Sky);
        Triangle(t,-20,0,200,180,330,0,Mountain);
        Triangle(t,130,0,340,230,550,0,Ridge);
        Triangle(t,300,0,445,150,570,0,Mountain);
        Rect(t,0,0,width,47,Ground); t.Apply(false,false); return t;
    }
    internal static Texture2D RenderGrid(DateTime displayedMonth, DateTime today, int width, int height)
    {
        Texture2D t=NewTexture(width,height); Fill(t,Paper); CultureInfo culture=CultureInfo.CurrentCulture;
        string month=culture.DateTimeFormat.GetMonthName(displayedMonth.Month).ToUpper(culture);
        Rect(t,38,height-118,width-76,4,Ink);
        DayOfWeek firstDay=culture.DateTimeFormat.FirstDayOfWeek; string[] shortest=culture.DateTimeFormat.ShortestDayNames;
        int cw=(width-70)/7,left=35;
        DateTime first=new(displayedMonth.Year,displayedMonth.Month,1); int start=((int)first.DayOfWeek-(int)firstDay+7)%7;
        using(var text=new CalendarTextCanvas(t))
        {
            text.Center(month,width/2-52,height-45,57,Ink,CalendarFontWeight.SemiBold);
            text.Right(displayedMonth.Year.ToString(culture),width-38,height-45,39,Ink,CalendarFontWeight.SemiBold);
            for(int col=0;col<7;col++){DayOfWeek wd=(DayOfWeek)(((int)firstDay+col)%7);string label=shortest[(int)wd].Substring(0,1).ToUpper(culture);text.Center(label,left+col*cw+cw/2,height-143,29,Weekend(wd)?Red:Ink,CalendarFontWeight.Medium);}
            for(int day=1;day<=DateTime.DaysInMonth(displayedMonth.Year,displayedMonth.Month);day++){int slot=start+day-1,col=slot%7,row=slot/7,cx=left+col*cw+cw/2,cy=height-210-row*65;DateTime cell=first.AddDays(day-1);text.Center(day.ToString(culture),cx,cy,32,Weekend(cell.DayOfWeek)?Red:Ink,CalendarFontWeight.Medium);}
            text.Apply();
        }
        for(int day=1;day<=DateTime.DaysInMonth(displayedMonth.Year,displayedMonth.Month);day++){int slot=start+day-1,col=slot%7,row=slot/7,cx=left+col*cw+cw/2,cy=height-225-row*65;DateTime cell=first.AddDays(day-1);CalendarDoodles.DrawDateMark(t,cell,cx,cy+11,44,34);if(cell<today)HandX(t,cx,cy+12,17,Red,cell);}
        t.Apply(false,false); return t;
    }
    internal static Texture2D ComposeViewer(Texture artTexture, DateTime displayedMonth, DateTime today, int width, int height)
    {
        int artHeight=height-width*600/700;
        Texture2D result=NewTexture(width,height);
        Texture2D monthGrid=RenderGrid(displayedMonth,today,width,height-artHeight);
        Color32[] output=new Color32[width*height];
        Color32[] gridPixels=monthGrid.GetPixels32();
        Array.Copy(gridPixels,0,output,0,gridPixels.Length);
        Texture source=artTexture;
        Texture2D fallback=null;
        if(source==null){fallback=RenderArt(512,255);source=fallback;}
        float sourceAspect=(float)source.width/source.height,targetAspect=(float)width/artHeight;
        float uScale=1f,vScale=1f,uOffset=0f,vOffset=0f;
        if(sourceAspect>targetAspect){uScale=targetAspect/sourceAspect;uOffset=(1f-uScale)*.5f;}
        else if(sourceAspect<targetAspect){vScale=sourceAspect/targetAspect;vOffset=(1f-vScale)*.5f;}
        // BOXROOM's custom pictures can be non-readable GPU textures. Copy through
        // a RenderTexture so the focus viewer uses the actual painted art instead
        // of treating it as unreadable and falling back to the mountain graphic.
        RenderTexture rt=RenderTexture.GetTemporary(width,artHeight,0,RenderTextureFormat.ARGB32);
        RenderTexture previous=RenderTexture.active;
        Texture2D readable=NewTexture(width,artHeight);
        try
        {
            Graphics.Blit(source,rt,new Vector2(uScale,vScale),new Vector2(uOffset,vOffset));
            RenderTexture.active=rt;
            readable.ReadPixels(new Rect(0,0,width,artHeight),0,0,false);
            readable.Apply(false,false);
            Color32[] artPixels=readable.GetPixels32();
            Array.Copy(artPixels,0,output,(height-artHeight)*width,artPixels.Length);
        }
        finally
        {
            RenderTexture.active=previous;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.Destroy(readable);
            if(fallback!=null) UnityEngine.Object.Destroy(fallback);
        }
        result.SetPixels32(output);result.Apply(false,false);UnityEngine.Object.Destroy(monthGrid);return result;
    }
    internal static Sprite CreateIcon(){DateTime now=DateTime.Now.Date;Texture2D t=Render(now,now,256,352);return Sprite.Create(t,new Rect(0,0,256,352),new Vector2(.5f,.5f));}
    private static bool Weekend(DayOfWeek d)=>d is DayOfWeek.Saturday or DayOfWeek.Sunday;
    private static Texture2D NewTexture(int w,int h){Texture2D t=new(w,h,TextureFormat.RGBA32,false);t.filterMode=FilterMode.Bilinear;t.wrapMode=TextureWrapMode.Clamp;return t;}
    private static void Fill(Texture2D t,Color32 c){Color32[] p=new Color32[t.width*t.height];Array.Fill(p,c);t.SetPixels32(p);}
    private static void Rect(Texture2D t,int x,int y,int w,int h,Color32 c){for(int py=Math.Max(0,y);py<Math.Min(t.height,y+h);py++)for(int px=Math.Max(0,x);px<Math.Min(t.width,x+w);px++)t.SetPixel(px,py,c);}
    private static void Triangle(Texture2D t,int x1,int y1,int x2,int y2,int x3,int y3,Color32 c){int minX=Math.Max(0,Math.Min(x1,Math.Min(x2,x3))),maxX=Math.Min(t.width-1,Math.Max(x1,Math.Max(x2,x3))),minY=Math.Max(0,Math.Min(y1,Math.Min(y2,y3))),maxY=Math.Min(t.height-1,Math.Max(y1,Math.Max(y2,y3)));float area=Edge(x1,y1,x2,y2,x3,y3);for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++){float a=Edge(x2,y2,x3,y3,x,y),b=Edge(x3,y3,x1,y1,x,y),d=Edge(x1,y1,x2,y2,x,y);if((area>=0&&a>=0&&b>=0&&d>=0)||(area<0&&a<=0&&b<=0&&d<=0))t.SetPixel(x,y,c);}}
    private static void HandX(Texture2D t,int cx,int cy,int radius,Color32 color,DateTime day){System.Random r=new(day.Year*10000+day.Month*100+day.Day+71);int lean=r.Next(-2,3);for(int i=-radius;i<=radius;i++){int wobble=(i%5==0)?r.Next(-1,2):0;Rect(t,cx+i-1,cy+i+lean+wobble-1,3,3,color);Rect(t,cx+i-1,cy-i-lean-wobble-1,3,3,color);}}
    private static float Edge(float ax,float ay,float bx,float by,float px,float py)=>(px-ax)*(by-ay)-(py-ay)*(bx-ax);
    private static int TextWidth(string s,int scale)=>Math.Max(0,s.Length*6*scale-scale);
    private static void DrawCentered(Texture2D t,string s,int cx,int y,int scale,Color32 c)=>Draw(t,s,cx-TextWidth(s,scale)/2,y,scale,c);
    private static void DrawRight(Texture2D t,string s,int right,int y,int scale,Color32 c)=>Draw(t,s,right-TextWidth(s,scale),y,scale,c);
    private static void Draw(Texture2D t,string s,int x,int y,int scale,Color32 c){int cursor=x;foreach(char raw in s){char ch=char.ToUpperInvariant(raw);if(Font.TryGetValue(ch,out string[] g))for(int row=0;row<7;row++)for(int col=0;col<5;col++)if(g[6-row][col]=='1')Rect(t,cursor+col*scale,y+row*scale,scale,scale,c);cursor+=6*scale;}}
}
