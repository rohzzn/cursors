using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cursors;

/// <summary>The language the user picked. Auto follows the Windows display language.</summary>
internal enum AppLanguage
{
    Auto,
    Chinese,
    English,
}

/// <summary>Stable ids for link-import failures, so the UI can branch without comparing translated text.</summary>
internal enum ImportError
{
    None,
    NotALink,
    NotCursorFile,
    ImportFailed,
    NoDownloadOnPage,
    TooLarge,
    LinkNotFound,
    HttpStatus,
    Timeout,
    NoConnection,
    SecureConnection,
    DownloadFailed,
}

/// <summary>
/// Every user-visible string in the app. English values are the defaults; the Chinese instance overrides
/// only what it translates, so a missing entry falls back to English instead of showing an empty label.
/// Format placeholders ({0}, {1}) must stay in the same order in every language.
/// </summary>
internal sealed class Strings
{
    // App and title bar
    public string AppTitle = "Cursors";
    public string ShortcutDescription = "One-click cursor packs for Windows";
    public string Restore = "Restore default";
    public string SearchCue = "Search packs";
    public string LangAuto = "Use system language";
    public string LangAutoShort = "Auto";
    public string LangChinese = "简体中文";
    public string LangChineseShort = "中文";
    public string LangEnglish = "English";
    public string LangEnglishShort = "EN";
    public string LangChangedChinese = "已切换为简体中文";
    public string LangChangedEnglish = "Switched to English";
    public string LangAutoToast = "Following the system language";
    public string ThemeAuto = "Use system theme";
    public string ThemeLight = "Light";
    public string ThemeDark = "Dark";
    public string ThemeAutoToast = "Following the Windows theme";
    public string ThemeLightToast = "Switched to the light theme";
    public string ThemeDarkToast = "Switched to the dark theme";

    // Sidebar
    public string NavLibrary = "Library";
    public string NavAll = "All packs";
    public string NavBuiltIn = "Built-in";
    public string NavCommunity = "Community";
    public string NavMine = "My packs";
    public string NavStyles = "Styles";

    // Header, sorting and filters
    public string SortPopular = "Most downloaded";
    public string SortTopRated = "Top rated";
    public string SortName = "Name";
    public string Filters = "Filters";
    public string FiltersCount = "Filters · {0}";
    public string CountFull = "{0} {1}";
    public string CountPartial = "{0} of {1} {2}";
    public string NounPack = "pack";
    public string NounPacks = "packs";
    public string NounSet = "set";
    public string NounSets = "sets";
    public string MenuSortPopular = "Most downloaded";
    public string MenuSortRated = "Top rated first";
    public string MenuSortName = "Name (A to Z)";
    public string MenuFilterFull = "Full sets, all 15 cursors";
    public string MenuFilterAnimated = "Animated";
    public string MenuFilterTopRated = "Top rated";
    public string MenuFilterOpen = "Open licenses (public domain, CC BY)";
    public string MenuClearFilters = "Clear filters";

    // Empty state
    public string EmptyNoMatch = "No packs match “{0}”";
    public string EmptyNoFilterMatch = "No sets match these filters";
    public string EmptyNothing = "Nothing here yet";
    public string EmptyTipBoth = "Try turning filters off, or another name, style or author";
    public string EmptyTipView = "Try All packs, or another name, style or author";
    public string EmptyTipQuery = "Try a pack name, style or author";
    public string EmptyTipFilters = "Turn some filters off to see more sets";
    public string EmptyTipAdd = "Packs you add appear here";

    // Cards and adding packs
    public string AddCard = "Add pack";
    public string AddFromFiles = "From files…\tCtrl+O";
    public string AddFromLink = "From a link…\tCtrl+L";
    public string DropOverlay = "Drop to add cursor packs";
    public string AddDialogTitle = "Add cursor packs";
    public string AddDialogFilter = "Cursor packs (*.zip, *.inf, *.cur, *.ani)|*.zip;*.inf;*.cur;*.ani|All files (*.*)|*.*";
    public string LinkDialogTitle = "Add from a link";
    public string LinkDialogHint = "Paste a link to a cursor set page (for example on rw-designer.com), or to a .zip, .cur or .ani file.";
    public string LinkImport = "Import";
    public string LinkCancel = "Cancel";

    // Card context menu
    public string CtxApply = "Apply";
    public string CtxDownloadApply = "Download and apply";
    public string CtxVisitSet = "Visit set page";
    public string CtxVisitProject = "Visit project page";
    public string CtxOpenFolder = "Open folder";
    public string CtxRemove = "Remove from library";

    // Toasts
    public string ToastRestored = "Windows default cursors restored";
    public string ToastApplyFail = "Couldn't apply “{0}”";
    public string ToastNoCursorFiles = "No cursor files found";
    public string ToastAlready = "Already in your library";
    public string ToastAdded = "Added “{0}”";
    public string ToastAddedMany = "Added {0} cursor packs";
    public string ToastRemoveFail = "Couldn't remove “{0}”";
    public string ToastRemoved = "Removed “{0}”";
    public string ToastStillLink = "Still importing the last link";
    public string ToastDownloading = "Downloading cursors…";
    public string ToastAddedLicense = "Added “{0}” · {1}";
    public string ToastStillDownloading = "Still downloading “{0}”";
    public string ToastDownloadingPack = "Downloading “{0}”…";
    public string ToastUnavailable = "“{0}” is no longer available";
    public string ToastNoCursorsIn = "No cursors found in “{0}”";
    public string ToastApplied = "Applied “{0}”";
    public string ToastAppliedLicense = "Applied “{0}” · {1}";
    public string NoPreview = "No preview";

    // Pack names and catalog
    public string WindowsDefault = "Windows Default";
    public string PreviousCursors = "Previous cursors";
    public string UnnamedPack = "Cursor pack";
    public string DownloadOne = "{0} download";
    public string DownloadsMany = "{0} downloads";
    public string ErrNotALink = "That isn't a web link";
    public string ErrNotCursorFile = "That link doesn't lead to a .zip, .cur or .ani file";
    public string ErrImportFailed = "Couldn't import that link";
    public string ErrNoDownloadOnPage = "No cursor download was found on that page";
    public string ErrTooLarge = "That download is too large for a cursor set";
    public string ErrLinkNotFound = "That link wasn't found";
    public string ErrHttpStatus = "The site answered {0} {1}";
    public string ErrTimeout = "The download timed out";
    public string ErrNoConnection = "Couldn't connect. Check your internet connection";
    public string ErrSecureConnection = "Couldn't make a secure connection to that site";
    public string ErrDownloadFailed = "The download failed";
}

/// <summary>Holds the active language and resolves every piece of UI text.</summary>
internal static class L
{
    private static readonly Strings English = new();

    private static readonly Strings Chinese = new()
    {
        AppTitle = "光标",
        ShortcutDescription = "一键切换 Windows 光标套装",
        Restore = "恢复默认",
        SearchCue = "搜索光标包",
        LangAuto = "跟随系统语言",
        LangAutoShort = "自动",
        LangChinese = "简体中文",
        LangChineseShort = "中文",
        LangEnglish = "English",
        LangEnglishShort = "EN",
        LangChangedChinese = "已切换为简体中文",
        LangChangedEnglish = "已切换为 English",
        LangAutoToast = "已跟随系统语言",
        ThemeAuto = "跟随系统主题",
        ThemeLight = "浅色",
        ThemeDark = "深色",
        ThemeAutoToast = "已跟随 Windows 主题",
        ThemeLightToast = "已切换为浅色主题",
        ThemeDarkToast = "已切换为深色主题",

        NavLibrary = "光标库",
        NavAll = "全部光标包",
        NavBuiltIn = "内置",
        NavCommunity = "社区",
        NavMine = "我的光标包",
        NavStyles = "风格",

        SortPopular = "下载最多",
        SortTopRated = "评分最高",
        SortName = "名称",
        Filters = "筛选",
        FiltersCount = "筛选 · {0}",
        CountFull = "{0} {1}",
        CountPartial = "{0} / {1} {2}",
        NounPack = "个光标包",
        NounPacks = "个光标包",
        NounSet = "套",
        NounSets = "套",
        MenuSortPopular = "下载最多",
        MenuSortRated = "评分最高优先",
        MenuSortName = "名称（A 到 Z）",
        MenuFilterFull = "完整套装（全部 15 个光标）",
        MenuFilterAnimated = "动态光标",
        MenuFilterTopRated = "评分最高",
        MenuFilterOpen = "开放许可（公有领域、CC BY）",
        MenuClearFilters = "清除筛选",

        EmptyNoMatch = "没有匹配“{0}”的光标包",
        EmptyNoFilterMatch = "没有符合筛选条件的光标套装",
        EmptyNothing = "这里还空着",
        EmptyTipBoth = "试试关闭筛选，或换个名称、风格、作者",
        EmptyTipView = "试试「全部光标包」，或换个名称、风格、作者",
        EmptyTipQuery = "试试输入名称、风格或作者",
        EmptyTipFilters = "关闭部分筛选可以看到更多套装",
        EmptyTipAdd = "添加的光标包会显示在这里",

        AddCard = "添加光标包",
        AddFromFiles = "从文件添加…\tCtrl+O",
        AddFromLink = "从链接添加…\tCtrl+L",
        DropOverlay = "松开以添加光标包",
        AddDialogTitle = "添加光标包",
        AddDialogFilter = "光标包 (*.zip, *.inf, *.cur, *.ani)|*.zip;*.inf;*.cur;*.ani|所有文件 (*.*)|*.*",
        LinkDialogTitle = "从链接添加",
        LinkDialogHint = "粘贴光标套装页面链接（例如 rw-designer.com 上的页面），或 .zip、.cur、.ani 文件的链接。",
        LinkImport = "导入",
        LinkCancel = "取消",

        CtxApply = "应用",
        CtxDownloadApply = "下载并应用",
        CtxVisitSet = "访问套装页面",
        CtxVisitProject = "访问项目页面",
        CtxOpenFolder = "打开所在文件夹",
        CtxRemove = "从光标库中移除",

        ToastRestored = "已恢复 Windows 默认光标",
        ToastApplyFail = "无法应用“{0}”",
        ToastNoCursorFiles = "未找到光标文件",
        ToastAlready = "光标库中已存在",
        ToastAdded = "已添加“{0}”",
        ToastAddedMany = "已添加 {0} 个光标包",
        ToastRemoveFail = "无法移除“{0}”",
        ToastRemoved = "已移除“{0}”",
        ToastStillLink = "上一个链接仍在导入中",
        ToastDownloading = "正在下载光标…",
        ToastAddedLicense = "已添加“{0}” · {1}",
        ToastStillDownloading = "“{0}”仍在下载中",
        ToastDownloadingPack = "正在下载“{0}”…",
        ToastUnavailable = "“{0}”已不可用",
        ToastNoCursorsIn = "“{0}”中未找到光标",
        ToastApplied = "已应用“{0}”",
        ToastAppliedLicense = "已应用“{0}” · {1}",
        NoPreview = "暂无预览",

        WindowsDefault = "Windows 默认",
        PreviousCursors = "之前的光标",
        UnnamedPack = "光标包",
        DownloadOne = "{0} 次下载",
        DownloadsMany = "{0} 次下载",
        ErrNotALink = "这不是有效的网页链接",
        ErrNotCursorFile = "该链接不是 .zip、.cur 或 .ani 文件",
        ErrImportFailed = "无法导入该链接",
        ErrNoDownloadOnPage = "该页面未找到光标下载地址",
        ErrTooLarge = "下载内容过大，不像光标套装",
        ErrLinkNotFound = "该链接未找到",
        ErrHttpStatus = "网站返回 {0} {1}",
        ErrTimeout = "下载超时",
        ErrNoConnection = "无法连接，请检查网络连接",
        ErrSecureConnection = "无法与该网站建立安全连接",
        ErrDownloadFailed = "下载失败",
    };

    /// <summary>Category ids come from pack.ini and stay in English internally; only their labels change.</summary>
    private static readonly Dictionary<string, string> ChineseCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Windows"] = "Windows",
        ["Minimal"] = "简约",
        ["macOS"] = "macOS",
        ["Retro"] = "复古",
        ["Pixel & Gaming"] = "像素与游戏",
        ["Neon"] = "霓虹",
        ["Glass"] = "玻璃",
        ["Cute"] = "可爱",
        ["Animated"] = "动态",
        ["Community"] = "社区",
        ["Added"] = "已添加",
    };

    public static Strings S { get; private set; } = English;
    public static AppLanguage Selection { get; private set; } = AppLanguage.Auto;
    public static bool IsChinese { get; private set; }

    public static string StoreValue => Selection switch
    {
        AppLanguage.Chinese => "zh-CN",
        AppLanguage.English => "en",
        _ => "auto",
    };

    /// <summary>Read once at startup, before any UI is created.</summary>
    public static void Load(string stored)
    {
        Selection = stored switch
        {
            "zh-CN" or "zh" or "chinese" => AppLanguage.Chinese,
            "en" or "english" => AppLanguage.English,
            _ => AppLanguage.Auto,
        };
        Refresh();
    }

    public static void Set(AppLanguage language)
    {
        Selection = language;
        Refresh();
    }

    private static void Refresh()
    {
        bool systemChinese = string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase);
        IsChinese = Selection == AppLanguage.Chinese || (Selection == AppLanguage.Auto && systemChinese);
        S = IsChinese ? Chinese : English;
    }

    public static string F(string format, params object[] args)
    {
        try { return string.Format(CultureInfo.CurrentCulture, format, args); }
        catch (FormatException) { return format; }
    }

    /// <summary>Display name for an internal category id. Unknown ids are user data and shown as-is.</summary>
    public static string Category(string key) =>
        key != null && IsChinese && ChineseCategories.TryGetValue(key, out string name) ? name : key ?? "";

    /// <summary>All names for a category, so searching works in either language without invalidating cached keys.</summary>
    public static string CategorySearchAliases(string key) =>
        key != null && ChineseCategories.TryGetValue(key, out string name) ? key + " " + name : key ?? "";

    public static string Count(int shown, int total, bool sets)
    {
        string totalText = total.ToString("N0", CultureInfo.CurrentCulture);
        string shownText = shown.ToString("N0", CultureInfo.CurrentCulture);
        string noun = IsChinese
            ? (sets ? S.NounSets : S.NounPacks)
            : (sets ? (shown == 1 ? S.NounSet : S.NounSets) : (shown == 1 ? S.NounPack : S.NounPacks));
        return shown == total
            ? F(S.CountFull, shownText, noun)
            : F(S.CountPartial, shownText, totalText, noun);
    }

    public static string Downloads(int count) =>
        F(count == 1 ? S.DownloadOne : S.DownloadsMany, count.ToString("N0", CultureInfo.CurrentCulture));

    public static string ImportMessage(ImportError error, params object[] args) => error switch
    {
        ImportError.NotALink => S.ErrNotALink,
        ImportError.NotCursorFile => S.ErrNotCursorFile,
        ImportError.ImportFailed => S.ErrImportFailed,
        ImportError.NoDownloadOnPage => S.ErrNoDownloadOnPage,
        ImportError.TooLarge => S.ErrTooLarge,
        ImportError.LinkNotFound => S.ErrLinkNotFound,
        ImportError.HttpStatus => F(S.ErrHttpStatus, args),
        ImportError.Timeout => S.ErrTimeout,
        ImportError.NoConnection => S.ErrNoConnection,
        ImportError.SecureConnection => S.ErrSecureConnection,
        ImportError.DownloadFailed => S.ErrDownloadFailed,
        _ => S.ErrImportFailed,
    };
}
