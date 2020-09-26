using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

using Gosub.Zurfur.Compiler;
using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Build
{

    /// <summary>
    /// Build package is responsible for building all the files under
    /// a subdirectory.  On the outside, it is single threaded running
    /// on the UI thread.  Underneath, it uses threads for anything
    /// CPU intensive or async for anything IO related.
    /// </summary>
    public class BuildPackage
    {
        CancellationTokenSource mCancel = new CancellationTokenSource();
        public event EventHandler BuildChanged;


        public void Cancel()
        {
            mCancel.Cancel();
        }

        public async Task Build(string dir)
        {
            var package = new PackageInfo(BuildManager.FixPathName(dir));
            var dt1 = DateTime.Now;
            await Task.Run(() => EnumerateDir(package.Dir, package));
            BuildChangedInternal();
            var dt2 = DateTime.Now;
            await Task.Run(() => Read(package));
            BuildChangedInternal();
            var dt3 = DateTime.Now;
            await Task.Run(() => Lex(package));
            BuildChangedInternal();
            var dt4 = DateTime.Now;
            await Task.Run(() => Parse(package));
            BuildChangedInternal();
            var dt5 = DateTime.Now;
            await Task.Run(() => GenerateHeader(package));
            BuildChangedInternal();
            var dt6 = DateTime.Now;

            var enumTime = dt2 - dt1;
            var readTime = dt3 - dt2;
            var lexTime = dt4 - dt3;
            var parseTime = dt5 - dt4;
            var genHeadTime = dt6 - dt5;
            var tt = dt6 - dt1;
        }

        /*public async Task<Lexer> GetLexer(string fileName)
        {

        }*/

        void BuildChangedInternal()
        {
            BuildChanged?.Invoke(this, EventArgs.Empty);
        }


        void EnumerateDir(string dir, PackageInfo package)
        {
            if (!Directory.Exists(dir))
                return;
            foreach (var file in Directory.GetFiles(dir))
                package.Files[file] = new FileInfo(file);
            foreach (var d in Directory.GetDirectories(dir))
            {
                package.SubDirs[d] = true;
                EnumerateDir(d, package);
            }
        }


        void Read(PackageInfo package)
        {
            foreach (var fi in package.Files)
            {
                if (mCancel.IsCancellationRequested)
                    break;

                switch (fi.Value.ExtensionLc)
                {
                    case ".zurf":
                    case ".md":
                    case ".txt":
                    case ".json":
                    case ".htm":
                    case ".html":
                        fi.Value.Bytes = File.ReadAllBytes(fi.Value.PathName);
                        fi.Value.Generation++;
                        break;
                }
            }
        }

        void Lex(PackageInfo package)
        {
            foreach (var fi in package.Files)
            {
                if (mCancel.IsCancellationRequested)
                    break;

                switch (fi.Value.ExtensionLc)
                {
                    case ".zurf":
                        var zurfLex = new Lexer(new ScanZurf());
                        zurfLex.ScanLines(new MemoryStream(fi.Value.Bytes));
                        fi.Value.Lexer = zurfLex;
                        fi.Value.Generation++;
                        break;
                    case ".md":
                    case ".txt":
                    case ".json":
                    case ".htm":
                    case ".html":
                        var textLex = new Lexer();
                        textLex.ScanLines(new MemoryStream(fi.Value.Bytes));
                        fi.Value.Lexer = textLex;
                        fi.Value.Generation++;
                        break;
                }
            }
        }

        void Parse(PackageInfo package)
        {
            foreach (var fi in package.Files)
            {
                if (mCancel.IsCancellationRequested)
                    break;

                switch (fi.Value.ExtensionLc)
                {
                    case ".zurf":
                        var zurfParse = new ParseZurf(fi.Value.Lexer);
                        fi.Value.Syntax = zurfParse.Parse();
                        fi.Value.Generation++;
                        break;
                    case ".json":
                        var jsonParse = new ParseJson(fi.Value.Lexer);
                        jsonParse.Parse();
                        fi.Value.Generation++;
                        break;
                }

            }
        }

        void GenerateHeader(PackageInfo package)
        {
            foreach (var fi in package.Files)
            {
                if (mCancel.IsCancellationRequested)
                    break;

                if (fi.Value.ExtensionLc == ".zurf")
                {
                    var sil = new SilGenHeader(fi.Value.PathName, fi.Value.Syntax);
                    sil.GenerateTypeDefinitions();
                    sil.MergeTypeDefinitions();
                    sil.GenerateHeader();
                    sil.GenerateCode();
                    fi.Value.SilHeader = sil;
                    fi.Value.Generation++;
                }
            }
        }

        class FileInfo
        {
            public string PathName;
            public string ExtensionLc; // Always lower case
            public int Generation;
            public byte[] Bytes;
            public Lexer Lexer;
            public SyntaxFile Syntax;
            public SilGenHeader SilHeader;

            public FileInfo(string fileName)
            {
                PathName = fileName;
                ExtensionLc = Path.GetExtension(PathName).ToLower();
            }
            public override string ToString()
            {
                return Path.GetFileName(PathName);
            }

        }

        class PackageInfo
        {
            public readonly string Dir;
            public readonly Dictionary<string, FileInfo> Files = new Dictionary<string, FileInfo>();
            public readonly Dictionary<string, bool> SubDirs = new Dictionary<string, bool>();

            public PackageInfo(string dir)
            {
                Dir = dir;
            }
        }



    }
}
