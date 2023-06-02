using Azure.AI.OpenAI;
using Azure;
using System;
using System.Configuration;
using File = System.IO.File;
using System.Text.RegularExpressions;
using static System.Net.WebRequestMethods;
using Microsoft.DeepDev;
using Xceed.Document.NET; // please use the appropriate license solution for the same
using Xceed.Words.NET; // please use the appropriate license solution for the same
using Microsoft.Identity.Client;

public class Program
{
    public static async Task Main(string[] args)
    {
        var appSettings = ConfigurationManager.AppSettings;
        string folderPath = appSettings["fromFolderName"];
        DirectoryInfo dir = new DirectoryInfo(folderPath);
        FileInfo[] files = dir.GetFiles();
       //string inputFile = @"";

        foreach (FileInfo file in files)
        {
            // Read stored procedure from files
            String storedProcedure = File.ReadAllText(Path.Combine(folderPath, file.Name));

            // Clean the stored procedure
            String cleanedProcedure = RemoveSqlComments(storedProcedure);
            
            // it is just to verify the clean stored proc if needed,  the file is not required for the processing  
            String outputcleanfile = Path.Combine(folderPath, "Cleaned_for_processing_" + file.Name);

            //Create a new file
            using (StreamWriter sw = File.CreateText(outputcleanfile))
            {
                //sw.WriteLine(result2);
                sw.WriteLine(cleanedProcedure);
                sw.Close();
            }

            String filetext = "";
            filetext = await ProcessInBatches(cleanedProcedure, Convert.ToInt16(appSettings["maxinputTokens"]), appSettings["azureopenaimodelname"]);
            // Create a new document.

            // Below is the open source library to create the word document
            //please use the appropriate license solution for the same
            String outputfile = Path.Combine(folderPath, "Summarized_By_OpenAI_" + file.Name + ".docx");
            using (var document = DocX.Create(outputfile))
            {
                // Insert a paragraph.
                document.InsertParagraph(filetext);

                // Save the document.
                document.Save();
            }
            
        }
    }
    /// <summary>
    /// This method is to clean the store procedure comment , extra spaces and new line etc.
    /// </summary>
    /// <param name="sql">Stored proc input</param>
    /// <returns>SQL without comments</returns>
    private static string RemoveSqlComments(String sql)
    {
        var lineComments = @"--(.*?)\r?\n";
        var lineCommentsNoLineBreak = @"--(.*?)[\r\n]";
        var blockComments = @"/\*(.*?)\*/";

        var noComments = Regex.Replace(sql, lineComments, "\r\n", RegexOptions.Singleline);
        noComments = Regex.Replace(noComments, lineCommentsNoLineBreak, "", RegexOptions.Singleline);
        noComments = Regex.Replace(noComments, blockComments, "", RegexOptions.Singleline);
        noComments = Regex.Replace(noComments, @"^\s*$\n|\r", string.Empty, RegexOptions.Multiline).TrimEnd();

        return noComments;
    }

    /// <summary>
    /// This methis is processing the input in batches and calling the OpenAI API to get the summary
    /// </summary>
    /// <param name="sql"> Cleaned SQL String</param>
    /// <param name="maxWords">Maximum prompt for input</param>
    /// <returns></returns>
    private static async Task<String> ProcessInBatches(String sql, int maxprompt, string modelname)
    {
        string[] lines = sql.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        string batch = string.Empty;
        int promptount = 0;
        String finaltext = "";
        var IM_START = "<|im_start|>";
        var IM_END = "<|im_end|>";
        string[] commandStarters = { "select", "insert", "update", "delete", "alter", "exec" };

        foreach (String line in lines)
        {
            var text = "<|im_start|>" + batch + "<|im_end|>";
            var specialTokens = new Dictionary<string, int>{
                                            { IM_START, 100264},
                                            { IM_END, 100265},
                                        };
            var tokenizer = TokenizerBuilder.CreateByModelName(modelname, specialTokens);
            var encoded = tokenizer.Encode(text, new HashSet<string>(specialTokens.Keys));
            promptount = encoded.Count;

            bool isCommandStarter = Array.Exists(commandStarters, command => line.Trim().StartsWith(command, StringComparison.OrdinalIgnoreCase));

            if ((promptount > maxprompt) && isCommandStarter )
            {              
                finaltext += await GetCodeSummary(batch);
                batch = line.Trim();
            }
            // you might need this case if you have a lot of lines that are not command starters and you reach to max prompt size
            //else if (promptount > (maxWords + 500)) 
            //{ 
            //    finaltext += await GetCodeSummary(batch);
            //    batch = line.Trim();
            //}
            else
            {
                batch += "\n" + line.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(batch))
        {
            finaltext += await GetCodeSummary(batch);
        }

        //Console.WriteLine(finaltext);
        return finaltext;
    }

    /// <summary>
    /// This method is calling the Azure OpenAI API to get the summary of the input (batch)
    /// </summary>
    /// <param name="batch"></param>
    /// <returns></returns>
    private static async Task<String> GetCodeSummary(String batch)
    {
        //String promptinputtext = "You are a Senior Microsoft SQL Database Developer. Summarize and explain the following Microsoft SQL code in simple english to a junior developer who is new to the project. Be informative and summarize the code and do not include any SQL statement in the summary:" + System.Environment.NewLine;
        String promptinputtext = "Can you explain in simple terms what it does?\n\nCode:\n{" + batch + "}";

        var appSettings = ConfigurationManager.AppSettings;
        OpenAIClient client = new OpenAIClient(
   new Uri(appSettings["APIuri"]),
   new AzureKeyCredential(appSettings["APIkey"]));
        Response<Completions> completionsResponse = await client.GetCompletionsAsync(
                 appSettings["engine"],
                new CompletionsOptions()
                {
                    Prompts = { promptinputtext },
                    Temperature = (float)0.6f,
                    MaxTokens = Convert.ToInt16(appSettings["maxoutputTokens"]),
                    //StopSequences = { "\n" },
                    NucleusSamplingFactor = (float)0.1,
                    FrequencyPenalty = (float)1,
                    PresencePenalty = (float)1,
                    GenerationSampleCount = 1,
                });
        // Replace this with the logic you want to apply to the batches.
        Completions completions = completionsResponse.Value;
        String outputtext = System.Environment.NewLine + completions.Choices[0].Text;
        return outputtext;
    }
}