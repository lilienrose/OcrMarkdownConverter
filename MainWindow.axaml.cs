using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using OpenCvSharp;
using Tesseract;
using Docnet.Core;
using Docnet.Core.Models;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using System.Collections.Generic;
using WeCantSpell.Hunspell;
using System.Text.Json;

namespace App
{
    public partial class MainWindow : Avalonia.Controls.Window
    {
        private const double LINE_Y_TOLERANCE = 5.0;
        private const double HEADING_SIZE_THRESHOLD = 1.5;
        private const double GARBLED_TEXT_THRESHOLD = 0.05;

        private WordList? _czechDictionary;
        private readonly object _dictLock = new();
        private List<(string Bad, string Good)> _ocrReplacements = new();

        // -------------------------------------------------------------------------------------------------------------------------------------------
        // Initializes the main window, loads OCR rules, and binds event handlers.
        // -------------------------------------------------------------------------------------------------------------------------------------------
        public MainWindow()
        {
            InitializeComponent();
            LoadOcrRules();
            
            var bttn = BtnTransfer;
            if (bttn != null)
            {
                bttn.Click += TransferOnClick;
            }
        }

        // -------------------------------------------------------------------------------------------------------------------------------------------
        // Loads custom OCR correction rules from a JSON configuration file, or falls back to defaults.
        // -------------------------------------------------------------------------------------------------------------------------------------------
        private void LoadOcrRules()
        {
            _ocrReplacements.Clear();
            string configPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ocr_rules.json");
            
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (data != null)
                    {
                        _ocrReplacements = data.Select(kvp => (kvp.Key, kvp.Value)).ToList();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading ocr_rules.json: {ex.Message}");
                }
            }
            
            LoadDefaultOcrRules();
            SaveDefaultOcrRules(configPath);
        }

        // -------------------------------------------------------------------------------------------------------------------------------------------
        // Populates the OCR replacements list with predefined default mapping rules.
        // -------------------------------------------------------------------------------------------------------------------------------------------
        private void LoadDefaultOcrRules()
        {
            _ocrReplacements = new List<(string Bad, string Good)>
            {
                ("i•", "ř"), ("i·", "ř"), ("n1", "m"), ("11", "m"), 
                ("in", "m"), ("ni", "m"), ("Bik", "sík"), ("Bík", "sík"), 
                ("B", "s"), ("f", "ř"), ("lry", "trý"), ("lr", "tr"), 
                ("slr", "str"), ("plate", "pláče"), ("pavl", "pato")
            };
        }

        // -------------------------------------------------------------------------------------------------------------------------------------------
        // Serializes and saves the current OCR mapping rules to a local JSON file.
        // -------------------------------------------------------------------------------------------------------------------------------------------
        private void SaveDefaultOcrRules(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var dict = _ocrReplacements.ToDictionary(x => x.Bad, x => x.Good);
                string json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not save default OCR rules: {ex.Message}");
            }
        }

        // -------------------------------------------------------------------------------------------------------------------------------------------
        // Retrieves or initializes the Czech Hunspell dictionary using a thread-safe lock.
        // -------------------------------------------------------------------------------------------------------------------------------------------
        private WordList? GetDictionary()
        {
            if (_czechDictionary != null) return _czechDictionary;
            
            lock (_dictLock)
            {
                if (_czechDictionary != null) return _czechDictionary;

                string affPath = Path.Combine(AppContext.BaseDirectory, "Assets", "cs_CZ.aff");
                string dicPath = Path.Combine(AppContext.BaseDirectory, "Assets", "cs_CZ.dic");

                if (File.Exists(affPath) && File.Exists(dicPath))
                {
                    _czechDictionary = WordList.CreateFromFiles(dicPath, affPath);
                }
            }
            return _czechDictionary;
        }

        // -------------------------------------------------------------------------------------------------------------------------------------------
        // Handles the transfer button click, opening a file dialog, processing the document asynchronously, and saving the Markdown output.
        // -------------------------------------------------------------------------------------------------------------------------------------------
        private async void TransferOnClick(object? sender, RoutedEventArgs e)
        {
            if (BtnTransfer == null || progressBar == null || TxtResult == null) 
                return;

            var storage = this.StorageProvider;
            var dialog = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions {
                Title = "Open PDF or Image",
                AllowMultiple = false,
                FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Supported Files") { Patterns = new[] { "*.pdf", "*.jpg", "*.jpeg", "*.png" } } }
            });

            if (dialog == null || dialog.Count == 0) return;
            string filePath = dialog[0].Path.LocalPath;

            progressBar.IsVisible = true;
            BtnTransfer.IsEnabled = false;
            TxtResult.Text = "Processing document, please wait...";

            try
            {
                string markdown = await Task.Run(() => ProcessDocument(filePath));
                string savePath = Path.ChangeExtension(filePath, ".md");
                await File.WriteAllTextAsync(savePath, markdown);

                TxtResult.Text = markdown + $"\n\n[INFO: Successfully saved to {savePath}]";
            }
            catch (Exception processError)
            {
                var realError = processError.InnerException?.Message ?? processError.Message;
                TxtResult.Text = $"An error occurred during processing:\n{realError}";
            }
            finally
            {
                progressBar.IsVisible = false;
                BtnTransfer.IsEnabled = true;
            }
        }

        // -------------------------------------------------------------------------------------------------------------------------------------------
        // Determines the file type (PDF vs Image) and routes the document to the appropriate text extraction engine (Direct PDF reader or OCR).
        // -------------------------------------------------------------------------------------------------------------------------------------------
        private string ProcessDocument(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower().Trim();

            if (ext == ".pdf")
            {
                var strBuildPdf = new StringBuilder();
                string tessdataPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tessdata");

                using var pdfDoc = PdfDocument.Open(filePath);
                using var docReader = DocLib.Instance.GetDocReader(filePath, new PageDimensions(4.0d));
                
                using var engine = new TesseractEngine(tessdataPath, "ces+eng", EngineMode.Default);

                for (int i = 0; i < docReader.GetPageCount(); i++)
                {
                    using var pageReader = docReader.GetPageReader(i);
                    string directText = pageReader.GetText()?.Trim() ?? "";

                    if (directText.Length > 50 && !IsGarbledText(directText))
                    {
                        var page = pdfDoc.GetPage(i + 1);
                        string rawDirect = FormatDirectText(page);
                        strBuildPdf.AppendLine(ReconstructParagraphs(rawDirect));
                    }
                    else
                    {
                        using var matBgra = Mat.FromPixelData(pageReader.GetPageHeight(), pageReader.GetPageWidth(), MatType.CV_8UC4, pageReader.GetImage());
                        using var matGray = new Mat();
                        Cv2.CvtColor(matBgra, matGray, ColorConversionCodes.BGRA2GRAY);
                        strBuildPdf.AppendLine(OcrProcessingMat(matGray, engine));
                    }
                }
                return strBuildPdf.ToString();
            }
            
            using var src = Cv2.ImRead(filePath, ImreadModes.Grayscale);
            if (src.Empty()) throw new Exception("Failed to load the image.");

            string singleTessPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tessdata");
            using var singleEngine = new TesseractEngine(singleTessPath, "ces+eng", EngineMode.Default);
            return OcrProcessingMat(src, singleEngine);
        }

        // -------------------------------------------------------------------------------------------------------------------------------------------
        // Parses directly extracted PDF text, groups letters into logical lines, detects formatting (bold, italics, lists), and identifies headings.
        // -------------------------------------------------------------------------------------------------------------------------------------------
        private string FormatDirectText(UglyToad.PdfPig.Content.Page page)
        {
            var strBuild = new StringBuilder();
            var headingLines = new List<string>();
            bool collectingMainHeading = true;
            double mainHeadingFontSize = 0;

            var allLetters = page.GetWords().SelectMany(w => w.Letters).ToList();
            double bodyFontSize = 0;
            if (allLetters.Any())
            {
                bodyFontSize = allLetters
                    .GroupBy(l => Math.Round(l.FontSize * 2) / 2)
                    .OrderByDescending(g => g.Count())
                    .First().Key;
            }

            var lineGroups = new List<List<Word>>();
            foreach (var word in page.GetWords().OrderByDescending(w => w.BoundingBox.Top))
            {
                var targetLine = lineGroups.FirstOrDefault(l => Math.Abs(l[0].BoundingBox.Bottom - word.BoundingBox.Bottom) < LINE_Y_TOLERANCE);
                if (targetLine != null)
                {
                    targetLine.Add(word);
                }
                else
                {
                    lineGroups.Add(new List<Word> { word });
                }
            }

            foreach (var lineGroup in lineGroups)
            {
                var words = lineGroup.OrderBy(w => w.BoundingBox.Left).ToList();
                if (!words.Any()) continue;

                string line = string.Join(" ", words.Select(w =>
                    w.Text.Replace("\u00ad", "").Replace("\ufffe", "")
                )).Trim();

                line = line.Replace("Š>ť", "Šť");
                line = Regex.Replace(line, @"\.{2,}", " ... ");

                if (string.IsNullOrWhiteSpace(line) || line.Length < 3) continue;
                if (!Regex.IsMatch(line, @"\p{L}{3,}")) continue;

                var letters = words.SelectMany(w => w.Letters).ToList();
                bool isItalic = letters.Count > 0 && letters.Count(l =>
                    (l.FontName ?? "").Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                    (l.FontName ?? "").Contains("Oblique", StringComparison.OrdinalIgnoreCase)) > letters.Count / 2;
                bool isBold = letters.Count > 0 && letters.Count(l =>
                    (l.FontName ?? "").Contains("Bold", StringComparison.OrdinalIgnoreCase)) > letters.Count / 2;

                if (line.StartsWith("•"))
                {
                    FlushHeadingIfNeeded(strBuild, headingLines, ref collectingMainHeading);
                    string content = line.Substring(1).Trim();
                    strBuild.AppendLine(isItalic ? $"- *{content}*" : $"- {content}");
                    continue;
                }

                if (Regex.IsMatch(line, @"^[–—]\s"))
                {
                    FlushHeadingIfNeeded(strBuild, headingLines, ref collectingMainHeading);
                    string content = Regex.Replace(line, @"^[–—]\s*", "").Trim();
                    strBuild.AppendLine(isItalic ? $"  - *{content}*" : $"  - {content}");
                    continue;
                }

                if (collectingMainHeading)
                {
                    double currentFontSize = letters.Count > 0 ? letters.Max(l => l.FontSize) : 0;

                    if (headingLines.Count == 0)
                    {
                        bool isLargerThanBody = currentFontSize > bodyFontSize + HEADING_SIZE_THRESHOLD;
                        bool isHeadingCandidate = isLargerThanBody || lineGroups.Count < 12 || isBold || isItalic;
                        bool looksLikeHeading = line.Length < 60
                            && !Regex.IsMatch(line, @"^[„""'«]")
                            && !char.IsLower(line[0])
                            && !Regex.IsMatch(line, @"[,;\.]$")
                            && !line.Contains("...");

                        if (looksLikeHeading && isHeadingCandidate)
                        {
                            mainHeadingFontSize = currentFontSize;
                            headingLines.Add(line);
                            continue;
                        }
                        else
                        {
                            collectingMainHeading = false;
                        }
                    }
                    else
                    {
                        bool looksLikeContinuation = line.Length < 60
                            && !Regex.IsMatch(line, @"^[„""'«]")
                            && !Regex.IsMatch(line, @"[\.]$")
                            && !line.Contains("...")
                            && (char.IsLower(line[0]) || Math.Abs(currentFontSize - mainHeadingFontSize) < HEADING_SIZE_THRESHOLD);

                        if (looksLikeContinuation)
                        {
                            headingLines.Add(line);
                            continue;
                        }
                        else
                        {
                            collectingMainHeading = false;
                            strBuild.AppendLine($"\n## {string.Join(" ", headingLines)}\n");
                        }
                    }
                }
                
                line = Regex.Replace(line, @"^[^\p{L}\d]+", "").Trim();
                if (line.Length < 3 || !Regex.IsMatch(line, @"\p{L}{3,}")) continue;

                var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (!line.Contains(' ') && line.All(c => !char.IsLetter(c) || char.IsUpper(c))) continue;

                if      (isItalic && isBold) strBuild.AppendLine($"***{line}***");
                else if (isItalic)           strBuild.AppendLine($"*{line}*");
                else if (isBold)             strBuild.AppendLine($"**{line}**");
                else                         strBuild.AppendLine(line);
            }

            if (collectingMainHeading && headingLines.Any())
            {
                strBuild.AppendLine($"\n## {string.Join(" ", headingLines)}\n");
            }

            return strBuild.ToString();
        }

        // -------------------------------------------------------------------------------------------------------------------------------------------
        // Flushes any accumulated heading lines into the Markdown output and updates the state.
        // -------------------------------------------------------------------------------------------------------------------------------------------
        private static void FlushHeadingIfNeeded(StringBuilder strBuild, List<string> headingLines, ref bool collectingMainHeading)
        {
            if (collectingMainHeading && headingLines.Any())
            {
                strBuild.AppendLine($"\n## {string.Join(" ", headingLines)}\n");
                collectingMainHeading = false;
            }
        }

        // -------------------------------------------------------------------------------------------------------------------------------------------
        // Analyzes the text to detect if it contains garbled data, measuring the ratio of malformed/mixed numeric tokens.
        // -------------------------------------------------------------------------------------------------------------------------------------------
        private bool IsGarbledText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;

            var tokens = text.Split((char[]) null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return true;

            int garbled = tokens.Count(t => Regex.IsMatch(t, @"[A-Za-zÁ-Žá-ž]\d|\d[A-Za-zÁ-Žá-ž]"));
            return (double)garbled / tokens.Length > GARBLED_TEXT_THRESHOLD;
        }

        // -------------------------------------------------------------------------------------------------------------------------------------------
        // Performs image pre-processing (binarization, blurring), executes the Tesseract OCR engine, and structure-formats the output lines.
        // -------------------------------------------------------------------------------------------------------------------------------------------
        private string OcrProcessingMat(Mat src, TesseractEngine engine)
        {
            var strBuild = new StringBuilder();

            using var cleanImg = new Mat();
            Cv2.Threshold(src, cleanImg, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            Cv2.MedianBlur(cleanImg, cleanImg, 3);

            string tempPath = Path.Combine(Path.GetTempPath(), $"ocr_{Guid.NewGuid()}.png");
            cleanImg.SaveImage(tempPath);

            try
            {
                using var pix = Pix.LoadFromFile(tempPath);
                using var page = engine.Process(pix);
                using var iter = page.GetIterator();
                iter.Begin();

                int prevBottom = -1, avgLineHeight = 30;

                do
                {
                    string line = iter.GetText(PageIteratorLevel.TextLine);
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    line = line.Trim();
                    line = Regex.Replace(line, @"^[a-z]\)\s+", "").Trim();
                    line = Regex.Replace(line, @"^[^\p{L}\d]+", "").Trim();
                    line = line.Replace("©", "").Replace("|", " ").Trim();
                    line = Regex.Replace(line, @"\s{2,}", " ").Trim();
                    line = Regex.Replace(line, @"[\[\]\{\}]", "").Trim();

                    if (line.Length < 4) continue;
                    if (!Regex.IsMatch(line, @"\p{L}{3,}")) continue;

                    int lineHeight = 0, lineTop = 0, lineBottom = 0;
                    if (iter.TryGetBoundingBox(PageIteratorLevel.TextLine, out Tesseract.Rect bbox))
                    {
                        lineHeight    = bbox.Height;
                        lineTop       = bbox.Y1;
                        lineBottom    = bbox.Y2;
                        avgLineHeight = (avgLineHeight + lineHeight) / 2;
                    }

                    if (prevBottom > 0 && lineTop > 0 && lineTop - prevBottom > avgLineHeight * 1.5)
                        strBuild.AppendLine();
                    prevBottom = lineBottom;

                    string core    = Regex.Replace(line, @"^[«*¢+\-—•·]\s*", "").Trim();
                    bool isAllCaps = core.Any(char.IsLetter) && core.Where(char.IsLetter).All(char.IsUpper);
                    bool isBullet  = Regex.IsMatch(line, @"^[«*¢+\-—•·]\s");

                    if      (isAllCaps && core.Length < 60 && lineHeight > avgLineHeight * 1.8) strBuild.Append($"\n# {core}\n\n");
                    else if (isAllCaps && core.Length < 60 && lineHeight > avgLineHeight * 1.3) strBuild.Append($"\n## {core}\n\n");
                    else if (isBullet)                                                         strBuild.AppendLine($"- {core}");
                    else                                                       strBuild.AppendLine(line);
                }
                while (iter.Next(PageIteratorLevel.TextLine));
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }

            return ReconstructParagraphs(strBuild.ToString());
        }

        // -------------------------------------------------------------------------------------------------------------------------------------------
        // Applies custom heuristics and replacement rules to correct common OCR reading errors before fallback spellchecking.
        // -------------------------------------------------------------------------------------------------------------------------------------------
        private string TryOcrHeuristics(string word, WordList? dict)
        {
            if (dict == null) return word; 
            if (dict.Check(word)) return word;

            var candidates = new HashSet<string>();

            if (word.Contains('-'))
            {
                candidates.Add(word.Replace("-", ""));
            }

            foreach (var rep in _ocrReplacements)
            {
                if (word.Contains(rep.Bad))
                {
                    candidates.Add(word.Replace(rep.Bad, rep.Good));
                }
                if (word.Contains(rep.Bad, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(word.Replace(rep.Bad, rep.Good, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (word.Contains('1'))
            {
                candidates.Add(word.Replace('1', 'l'));
                candidates.Add(word.Replace('1', 'i'));
                candidates.Add(word.Replace('1', 't'));
            }

            foreach (var cand in candidates)
            {
                if (dict.Check(cand)) return cand;

                var sugg = dict.Suggest(cand).FirstOrDefault();
                if (sugg != null && EditDistance(cand, sugg) <= 1)
                {
                    return sugg;
                }
            }

            return word; 
        }

        // -------------------------------------------------------------------------------------------------------------------------------------------
        // Processes a line word-by-word, strips punctuation/formatting, corrects spelling mistakes, and returns the reconstructed line.
        // -------------------------------------------------------------------------------------------------------------------------------------------
        private string SpellCorrectLine(string line)
        {
            var dict = GetDictionary();
            if (dict == null) return line; 

            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                string raw = words[i];
                if (!raw.Any(char.IsLetter)) continue;

                string prefix = string.Concat(raw.TakeWhile(c => !char.IsLetter(c) && !char.IsDigit(c)));
                string suffix = string.Concat(raw.Reverse().TakeWhile(c => !char.IsLetter(c) && !char.IsDigit(c)).Reverse());
                
                if (prefix.Length + suffix.Length >= raw.Length) continue;
                string word = raw[prefix.Length..(raw.Length - suffix.Length)];

                string cleaned = TryOcrHeuristics(word, dict);

                if (dict.Check(cleaned))
                {
                    words[i] = prefix + cleaned + suffix;
                    continue;
                }

                var suggestions = dict.Suggest(cleaned).Take(5).ToList();
                if (suggestions.Any())
                {
                    string best = suggestions
                        .OrderBy(s => EditDistance(cleaned, s))
                        .First();

                    if (EditDistance(cleaned, best) <= 2)
                    {
                        words[i] = prefix + best + suffix;
                    }
                    else
                    {
                        words[i] = prefix + cleaned + suffix;
                    }
                }
                else
                {
                    words[i] = prefix + cleaned + suffix;
                }
            }

            return string.Join(' ', words);
        }

        // -------------------------------------------------------------------------------------------------------------------------------------------
        // Calculates the Levenshtein distance (minimum edit operations) between two strings.
        // -------------------------------------------------------------------------------------------------------------------------------------------
        private static int EditDistance(string a, string b)
        {
            int[,] dp = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) dp[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    dp[i, j] = a[i - 1] == b[j - 1]
                        ? dp[i - 1, j - 1]
                        : 1 + Math.Min(dp[i - 1, j - 1], Math.Min(dp[i - 1, j], dp[i, j - 1]));
            return dp[a.Length, b.Length];
        }

        // -------------------------------------------------------------------------------------------------------------------------------------------
        // Reconstructs logical paragraphs from single-line streams, reconnects hyphenated words split at line ends, and runs spellchecking.
        // -------------------------------------------------------------------------------------------------------------------------------------------
        private string ReconstructParagraphs(string rawText)
        {
            var result = new StringBuilder();
            var wordsInParagraph = new List<string>();
            var dict = GetDictionary();

            void FlushParagraph()
            {
                if (wordsInParagraph.Count == 0) return;
                string para = string.Join(" ", wordsInParagraph);
                para = SpellCorrectLine(para); 
                result.AppendLine(para);
                result.AppendLine();
                wordsInParagraph.Clear();
            }

            foreach (var rawLine in rawText.Split('\n'))
            {
                string line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushParagraph();
                    continue;
                }

                if (line.StartsWith("#") || line.StartsWith("- ") || line.StartsWith("  - "))
                {
                    FlushParagraph();
                    result.AppendLine(line);
                    continue;
                }

                var currentLineWords = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (currentLineWords.Count == 0) continue;

                if (wordsInParagraph.Count > 0)
                {
                    string lastWordOfPrev = wordsInParagraph[^1];
                    string firstWordOfNew = currentLineWords[0];

                    if (lastWordOfPrev.EndsWith("-") && lastWordOfPrev.Length > 1)
                    {
                        string cleanPrev = lastWordOfPrev[..^1];
                        string combinedWithoutHyphen = cleanPrev + firstWordOfNew;

                        string testWord = TryOcrHeuristics(combinedWithoutHyphen, dict);

                        if (dict != null && (dict.Check(testWord) || dict.Suggest(testWord).Any(s => EditDistance(testWord, s) <= 2)))
                        {
                            wordsInParagraph[^1] = testWord;
                            currentLineWords.RemoveAt(0);
                        }
                        else
                        {
                            wordsInParagraph[^1] = combinedWithoutHyphen;
                            currentLineWords.RemoveAt(0);
                        }
                    }
                }

                wordsInParagraph.AddRange(currentLineWords);
            }

            FlushParagraph();
            return result.ToString().TrimEnd();
        }
    }
}