namespace LumaLyrics.Infrastructure;

internal static class PreparationProgressFile
{
    internal static void Write(string path,string json)
    {
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllText(temp,json);
            // Older running applications may briefly read without FileShare.Delete.
            // Retain the previous complete snapshot until the reader releases it.
            for(int attempt=0;;attempt++)
            {
                try{File.Move(temp,path,true);break;}
                catch(Exception error) when(attempt<10 && error is IOException or UnauthorizedAccessException){Thread.Sleep(20);}
            }
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
