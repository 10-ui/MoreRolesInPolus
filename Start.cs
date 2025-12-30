using System.Diagnostics;
using System.IO.Compression;

namespace Toa
{
    internal class Start
    {
        // パス設定（ここを書き換えるだけでOK）
        const string TargetExePath = @"D:\Steam\steamapps\common\Among Us NoS_dev\Among Us.exe";
        const string StartPath = @"D:\.dev\NebulaOnTheShipAU\MoreRolesInPolus\MoreRolesInPolus";
        const string ZipPath = @"D:\Steam\steamapps\common\Among Us NoS_dev\Addons\[Toa]MoreRolesInPolus.zip";

        static void Main(string[] args)
        {
            // --- 1. 引数がない場合のヘルプ表示 ---
            if (args.Length == 0)
            {
                ShowHelp();
                return;
            }

            string command = args[0].ToLower();

            // --- 2. 各コマンドの分岐処理 ---
            if (command == "zip")
            {
                // ZIP圧縮のみ実行
                RunZipProcess();
            }
            else if (command == "ziprun")
            {
                // ZIP圧縮 ＋ 1回だけ起動
                RunZipProcess();
                System.Console.WriteLine("\n--- 続けてゲームを起動します (1回) ---");
                RunLauncherProcess(1);
            }
            else if (int.TryParse(args[0], out int count))
            {
                // 指定された回数だけ起動（旧来の機能）
                RunLauncherProcess(count);
            }
            else
            {
                System.Console.WriteLine("無効な引数です。");
                ShowHelp();
            }

            System.Console.WriteLine("\nすべての処理が完了しました。何かキーを押すと終了します...");
            System.Console.ReadKey();
        }

        static void ShowHelp()
        {
            System.Console.WriteLine("--- 使い方 ---");
            System.Console.WriteLine("Toa.exe zip       : フォルダを圧縮するだけ");
            System.Console.WriteLine("Toa.exe ziprun    : 圧縮して、ゲームを1回だけ起動する ★追加");
            System.Console.WriteLine("Toa.exe [数値]    : ゲームを指定回数だけ起動する (例: Toa.exe 5)");
            System.Console.WriteLine("--------------");
            System.Console.WriteLine("\n何かキーを押すと終了します...");
            System.Console.ReadKey();
        }

        static void RunLauncherProcess(int count)
        {
            if (!File.Exists(TargetExePath))
            {
                System.Console.WriteLine($"エラー: ファイルが見つかりません: {TargetExePath}");
                return;
            }

            System.Console.WriteLine($"{TargetExePath} を {count} 回起動します。");

            for (int i = 1; i <= count; i++)
            {
                try
                {
                    System.Console.WriteLine($"[{i}/{count}] 起動中...");
                    Process.Start(new ProcessStartInfo(TargetExePath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"失敗: {ex.Message}");
                    break;
                }

                // 次の起動がある場合のみ待機
                if (i < count)
                {
                    System.Console.WriteLine("8秒待機...");
                    Thread.Sleep(8000);
                }
            }
        }

        static void RunZipProcess()
        {
            string rootFolderNameInZip = Path.GetFileNameWithoutExtension(ZipPath);

            try
            {
                if (File.Exists(ZipPath))
                {
                    File.Delete(ZipPath);
                    System.Console.WriteLine("既存のZIPを削除しました。");
                }

                if (!Directory.Exists(StartPath))
                {
                    System.Console.WriteLine("エラー: 圧縮元のフォルダが見つかりません。");
                    return;
                }

                System.Console.WriteLine("圧縮を開始します...");

                using (ZipArchive archive = ZipFile.Open(ZipPath, ZipArchiveMode.Create))
                {
                    DirectoryInfo di = new DirectoryInfo(StartPath);
                    foreach (FileInfo file in di.GetFiles("*", SearchOption.AllDirectories))
                    {
                        string relativePath = Path.GetRelativePath(StartPath, file.FullName);
                        string entryName = Path.Combine(rootFolderNameInZip, relativePath).Replace('\\', '/');
                        archive.CreateEntryFromFile(file.FullName, entryName, System.IO.Compression.CompressionLevel.Optimal);
                    }
                }
                System.Console.WriteLine($"圧縮完了: {ZipPath}");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"エラー: {ex.Message}");
            }
        }
    }
}