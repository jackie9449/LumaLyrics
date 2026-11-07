using LumaLyrics.Core;
namespace LumaLyrics.Infrastructure;
public readonly record struct LibrarySongKey(string Source,string PersistentId)
{
    public static IEqualityComparer<LibrarySongKey> Comparer {get;}=new KeyComparer();
    sealed class KeyComparer:IEqualityComparer<LibrarySongKey>
    {
        public bool Equals(LibrarySongKey a,LibrarySongKey b)=>StringComparer.OrdinalIgnoreCase.Equals(a.Source,b.Source)&&StringComparer.Ordinal.Equals(a.PersistentId,b.PersistentId);
        public int GetHashCode(LibrarySongKey key)=>HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Source),StringComparer.Ordinal.GetHashCode(key.PersistentId));
    }
}
public enum LibraryOriginalState { Missing,Unconfirmed,ManualPlain,Ready,Instrumental }
public enum LibraryChineseState { BlockedOriginal,NotRequired,Missing,Partial,Complete,Outdated }
public enum LibraryCoverState { Missing,Ready,HighResolution,Unreadable }
public sealed record LibraryStatusInput(LyricsDocument? Document,bool ConfirmedOriginal,bool ManualOriginal,ChineseTranslation? Translation,bool ManualTranslation,bool HasOlderTranslation,string? Revision,LibraryCoverState Cover);
public sealed record LibrarySongStatus(LibraryOriginalState Original,LibraryChineseState Chinese,int RequiredGroups,int TranslatedGroups,bool ManualChinese,bool AiDraft,LibraryCoverState Cover,string? SourceHash,string? Revision,string? Reason)
{
    public string CoverLabel=>"封面："+(Cover==LibraryCoverState.HighResolution?"高清":Cover==LibraryCoverState.Ready?"可用":Cover==LibraryCoverState.Unreadable?"不可读":"缺失");
    public string OriginalLabel=>Original switch{LibraryOriginalState.Ready=>"动态原词",LibraryOriginalState.Instrumental=>"纯音乐",LibraryOriginalState.ManualPlain=>"手动静态原词",LibraryOriginalState.Unconfirmed=>"原词待确认",_=>"缺原词"};
    public string ChineseLabel=>Chinese switch{LibraryChineseState.Complete=>$"中文完整 {TranslatedGroups}/{RequiredGroups}",LibraryChineseState.Partial=>$"中文部分 {TranslatedGroups}/{RequiredGroups}",LibraryChineseState.NotRequired=>"无需中文",LibraryChineseState.Outdated=>"原词已更新",LibraryChineseState.BlockedOriginal=>"需动态原词",_=>"缺中文译文"};
}
public sealed record LibraryManagedSong(LibrarySongKey Key,Track Track,string? BindingId,LibrarySongStatus Status)
{
    public string Label=>$"{Track.Title} · {Track.Artist} · {Track.Album}\n{Status.OriginalLabel}　{Status.ChineseLabel}　封面：{(Status.Cover==LibraryCoverState.HighResolution?"高清":Status.Cover==LibraryCoverState.Ready?"可用":Status.Cover==LibraryCoverState.Unreadable?"不可读":"缺失")}";
    public LibraryPreparationTarget Target=>new(Key,Track,BindingId,Status.SourceHash,Status.Revision);
}
public sealed record LibraryPreparationTarget(LibrarySongKey Key,Track Track,string? BindingId,string? SourceHash,string? ExpectedTranslationRevision);
public sealed record LibraryWorkspaceSnapshot(string ProfileRoot,string Source,long Generation,bool IsStale,IReadOnlyList<LibraryManagedSong> Songs,string? Message=null);
public static class LibraryWorkspaceProjection
{
    public static LibrarySongStatus Assess(LibraryStatusInput input)
    {
        var doc=input.Document;
        var original=doc?.Instrumental==true?LibraryOriginalState.Instrumental:doc is null?LibraryOriginalState.Missing:!doc.IsSynced?input.ManualOriginal?LibraryOriginalState.ManualPlain:LibraryOriginalState.Unconfirmed:input.ConfirmedOriginal?LibraryOriginalState.Ready:LibraryOriginalState.Unconfirmed;
        string? hash=doc is null?null:TranslationAlignment.Hash(doc.Raw);var translation=input.Translation;
        bool containsJapaneseOrKorean=doc?.Groups.Any(g=>g.Text.Any(c=>c is >= '\u3040' and <= '\u30ff' or >= '\uac00' and <= '\ud7af'))==true;
        var needed=original==LibraryOriginalState.Ready&&!TranslationAlignment.IsChinese(string.Join("\n",doc!.Groups.Select(g=>g.Text)))?doc!.Groups.Select((g,i)=>(g,i)).Where(x=>TranslationAlignment.NeedsChineseTranslation(x.g.Text,containsJapaneseOrKorean)).Select(x=>x.i).ToArray():[];
        bool current=translation?.SourceHash==hash&&translation is not null;var translated=current?translation!.Lines.Where(l=>!string.IsNullOrWhiteSpace(l.Text)).Select(l=>l.GroupIndex).ToHashSet():[];int count=needed.Count(translated.Contains);
        var chinese=original==LibraryOriginalState.Instrumental?LibraryChineseState.NotRequired:original!=LibraryOriginalState.Ready?LibraryChineseState.BlockedOriginal:needed.Length==0?LibraryChineseState.NotRequired:!current?input.HasOlderTranslation||translation is not null?LibraryChineseState.Outdated:LibraryChineseState.Missing:count==needed.Length?LibraryChineseState.Complete:count>0?LibraryChineseState.Partial:LibraryChineseState.Missing;
        return new(original,chinese,needed.Length,count,input.ManualTranslation,translation?.Draft==true,input.Cover,hash,input.Revision,null);
    }
}
