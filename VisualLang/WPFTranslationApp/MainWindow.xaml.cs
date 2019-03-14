using System;
using System.Windows;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Timers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using System.Collections.ObjectModel;
using Microsoft.Expression.Encoder.Devices;
using System.Drawing;
using System.Windows.Media;
using System.Threading;
using System.Collections;

namespace MSTranslatorTextDemo
{

    public partial class MainWindow : Window
    {
        // This sample uses the Cognitive Services subscription key for all services. To learn more about
        // authentication options, see: https://docs.microsoft.com/azure/cognitive-services/authentication.
        const string COGNITIVE_SERVICES_KEY = "ef0b37aefa9748fdae669e201b8ecbaa"; //Eamonn's key to Microsoft Cognitive Services
        const string SPELL_CHECK_KEY = "11b50d922c7b443d962e7631f983feb3"; //Eamonn's key to the microsoft spellcheck service. 
        const string OBJECT_RECOGNIZER_KEY = "6926aeac904449bab4a529027e187fe8";

        // Endpoints for Translator Text and Bing Spell Check
        const string OBJECT_RECOGNIZER_ENDPOINT = "https://canadacentral.api.cognitive.microsoft.com/";
        public static readonly string TEXT_TRANSLATION_API_ENDPOINT = "https://api.cognitive.microsofttranslator.com/{0}?api-version=3.0";
        const string BING_SPELL_CHECK_API_ENDPOINT = "https://api.cognitive.microsoft.com/bing/v7.0/spellcheck";


        // An array of language codes
        private string[] languageCodes;
        private static string authToken;

        // Dictionary to map language codes from friendly name (sorted case-insensitively on language name)
        private SortedDictionary<string, string> languageCodesAndTitles =
            new SortedDictionary<string, string>(Comparer<string>.Create((a, b) => string.Compare(a, b, true)));

        private static System.Timers.Timer tokenRefresh; //The timer to refresh the token 
        private static int tokenTimeout = 8 * 60 * 1000; //8 minutes

        String foreignLanguageText; //gets populated by the translateTextIntoLanguage() function. 
        String unknownObjectString = "Unknown Object";

        //variables associated with the minigame. 
        ArrayList knownObjects; //an arraylist to keep known objects in. 
        int miniGameKnownObjectLimit = 3; //must have this many items recognized in order to play. 
        System.Timers.Timer miniGameTimer = new System.Timers.Timer(1000); //a timer to update the time remaining on the main screen. 
        int timeRemaining = 30; //the number of seconds the user has to find the object. 
        String currentWordToFind = "";

        bool playingMiniGame = false;



        //Super Duper Computer Vision Client Instance
        private ComputerVisionClient computerVision;

        // Specify the features to return
        private static readonly List<VisualFeatureTypes> features =
            new List<VisualFeatureTypes>()
        {
            VisualFeatureTypes.Objects
        };


        public MainWindow()
        {
            //prepare the array
            knownObjects = new ArrayList();

            // at least show an error dialog if there's an unexpected error
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(HandleExceptions);

            computerVision = new ComputerVisionClient(
               new ApiKeyServiceClientCredentials(OBJECT_RECOGNIZER_KEY),
               new System.Net.Http.DelegatingHandler[] { });
            computerVision.Endpoint = OBJECT_RECOGNIZER_ENDPOINT;

            //Make sure the key is suitable. 
            if (COGNITIVE_SERVICES_KEY.Length != 32)
            {
                MessageBox.Show("One or more invalid API subscription keys.\n\n" +
                    "Put your keys in the *_API_SUBSCRIPTION_KEY variables in MainWindow.xaml.cs.",
                    "Invalid Subscription Key(s)", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Windows.Application.Current.Shutdown();
            }
            else
            {
                //Get the initial access token
                getAccessToken();

                //start the timer to get another token every 8 minutes. 
                startTimer();

                // Start GUI
                InitializeComponent();

                // Get languages for drop-downs
                GetLanguagesForTranslate();

                // Populate drop-downs with values from GetLanguagesForTranslate
                PopulateLanguageMenus();
            }

            //image capture setup
            this.DataContext = this;
            startPreview();
            styleGUI();




        }


        private void styleGUI()
        {
            TranslatedTextLabel.Content = "Translated Text will Appear Here.";
            DetectedObjectLabel.Content = "Detected Object Name will Appear Here";
        }

        /// <summary>
        /// Starts a timer that will refresh the authorization token at regular intervals. 
        /// </summary>
        private static void startTimer()
        {
            tokenRefresh = new System.Timers.Timer(tokenTimeout);
            tokenRefresh.AutoReset = true;
            tokenRefresh.Enabled = true;
            tokenRefresh.Elapsed += new ElapsedEventHandler(onTokenRefresh);
            tokenRefresh.Start();

        }

        /// <summary>
        /// Called when the timer elapses. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void onTokenRefresh(Object sender, ElapsedEventArgs e)
        {
            getAccessToken();
        }

        /// <summary>
        /// Gets a new access token
        /// </summary>
        private static void getAccessToken()
        {

            string authenticationURL = "https://api.cognitive.microsoft.com/sts/v1.0/issuetoken";


            // Create request to Detect languages with Translator Text
            HttpWebRequest getTokenRequest = (HttpWebRequest)WebRequest.Create(authenticationURL);
            getTokenRequest.Headers.Add("Ocp-Apim-Subscription-Key", COGNITIVE_SERVICES_KEY);
            getTokenRequest.ContentType = "application/json; charset=utf-8";
            getTokenRequest.Method = "POST";

            getTokenRequest.ContentType = "application/x-www-form-urlencoded";
            getTokenRequest.ContentLength = 0;

            // Send request
            string body = "";
            byte[] data = Encoding.UTF8.GetBytes(body);
            using (var requestStream = getTokenRequest.GetRequestStream())
                requestStream.Write(data, 0, data.Length);

            HttpWebResponse response = (HttpWebResponse)getTokenRequest.GetResponse();
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            var responseStream = response.GetResponseStream();
            authToken = new StreamReader(responseStream, Encoding.GetEncoding("utf-8")).ReadToEnd();

        }


        // Global exception handler to display error message and exit
        private static void HandleExceptions(object sender, UnhandledExceptionEventArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;
            MessageBox.Show("Caught " + e.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            System.Windows.Application.Current.Shutdown();
        }

        //*** ANALYZE LOCAL IMAGE ASYNC
        private async Task AnalyzeLocalAsync(string imagePath)
        {
            if (!File.Exists(imagePath))
            {
                Console.WriteLine(
                    "\nUnable to open or read localImagePath:\n{0} \n", imagePath);
                return;
            }

            using (Stream imageStream = File.OpenRead(imagePath))
            {
                ImageAnalysis analysis = await computerVision.AnalyzeImageInStreamAsync(
                    imageStream, features);
                String textToTranslate = GetObjectNameAndSetLabels(analysis);
                
                TranslateTextToForeignLanguage(textToTranslate);
                
                
            }
        }


        /// <summary>
        /// Populate the language menus
        /// </summary>
        private void PopulateLanguageMenus()
        {


            int count = languageCodesAndTitles.Count;
            foreach (string menuItem in languageCodesAndTitles.Keys)
            {

                ToLanguageComboBox.Items.Add(menuItem);
            }

            // Set default languages
            ToLanguageComboBox.SelectedItem = "English";
        }


        /// <summary>
        /// Spellcheck the word, if it's in english. 
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        private string CorrectSpelling(string text)
        {
            string uri = BING_SPELL_CHECK_API_ENDPOINT + "?mode=spell&mkt=en-US";

            // Create a request to Bing Spell Check API
            HttpWebRequest spellCheckWebRequest = (HttpWebRequest)WebRequest.Create(uri);
            spellCheckWebRequest.Headers.Add("Ocp-Apim-Subscription-Key", SPELL_CHECK_KEY);
            spellCheckWebRequest.Method = "POST";
            spellCheckWebRequest.ContentType = "application/x-www-form-urlencoded"; // doesn't work without this

            // Create and send the request
            string body = "text=" + System.Web.HttpUtility.UrlEncode(text);
            byte[] data = Encoding.UTF8.GetBytes(body);
            spellCheckWebRequest.ContentLength = data.Length;
            using (var requestStream = spellCheckWebRequest.GetRequestStream())
                requestStream.Write(data, 0, data.Length);

            HttpWebResponse response = (HttpWebResponse)spellCheckWebRequest.GetResponse();
           
            

            // Read and parse the JSON response; get spelling corrections
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            var responseStream = response.GetResponseStream();
            var jsonString = new StreamReader(responseStream, Encoding.GetEncoding("utf-8")).ReadToEnd();
            dynamic jsonResponse = serializer.DeserializeObject(jsonString);
            var flaggedTokens = jsonResponse["flaggedTokens"];

            // Construct sorted dictionary of corrections in reverse order (right to left)
            // This ensures that changes don't impact later indexes
            var corrections = new SortedDictionary<int, string[]>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
            for (int i = 0; i < flaggedTokens.Length; i++)
            {
                var correction = flaggedTokens[i];
                var suggestion = correction["suggestions"][0];  // consider only first suggestion
                if (suggestion["score"] > (decimal)0.7)         // take it only if highly confident
                    corrections[(int)correction["offset"]] = new string[]   // dict key   = offset
                        { correction["token"], suggestion["suggestion"] };  // dict value = {error, correction}
            }

            // Apply spelling corrections, in order, from right to left
            foreach (int i in corrections.Keys)
            {
                var oldtext = corrections[i][0];
                var newtext = corrections[i][1];

                // Apply capitalization from original text to correction - all caps or initial caps
                if (text.Substring(i, oldtext.Length).All(char.IsUpper)) newtext = newtext.ToUpper();
                else if (char.IsUpper(text[i])) newtext = newtext[0].ToString().ToUpper() + newtext.Substring(1);

                text = text.Substring(0, i) + newtext + text.Substring(i + oldtext.Length);
            }

            return text;
        }


        // ***** GET TRANSLATABLE LANGUAGE CODES
        private void GetLanguagesForTranslate()
        {
            // Send a request to get supported language codes
            string uri = String.Format(TEXT_TRANSLATION_API_ENDPOINT, "languages") + "&scope=translation";
            WebRequest WebRequest = WebRequest.Create(uri);
            WebRequest.Headers.Add("Authorization", "Bearer " + authToken);
            WebRequest.Headers.Add("Accept-Language", "en");
            WebResponse response = null;
            // Read and parse the JSON response
            response = WebRequest.GetResponse();
            using (var reader = new StreamReader(response.GetResponseStream(), UnicodeEncoding.UTF8))
            {
                var result = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(reader.ReadToEnd());
                var languages = result["translation"];

                languageCodes = languages.Keys.ToArray();
                foreach (var kv in languages)
                {
                    languageCodesAndTitles.Add(kv.Value["name"], kv.Key);
                }
            }
        }


        /// <summary>
        /// Gets the object name from the analysis, sets the relevant labels, and returns teh object name.
        /// </summary>
        /// <param name="analysis"></param>
        /// <returns>The name of the object in English.</returns>
        private string GetObjectNameAndSetLabels(ImageAnalysis analysis)
        {
            IList<DetectedObject> objects = analysis.Objects;
            string textToTranslate = "";
            string detectedObjectString = "";

            foreach (DetectedObject obj in objects)
            {
                textToTranslate += obj.ObjectProperty; //only translate object name
                detectedObjectString += "\"" + obj.ObjectProperty + "\" (" + (obj.Confidence * 100) + "% Confidence)";
            }

            if (textToTranslate == "")
            {
                textToTranslate = unknownObjectString;
                detectedObjectString = unknownObjectString;

            } else
            {
                //object was recognized, save it to the list.
                knownObjects.Add(textToTranslate);
            }

            DetectedObjectLabel.Content = detectedObjectString;

            if (playingMiniGame)
            {
                checkIfWordMatches(textToTranslate);
            }

            return textToTranslate;

        }


        private void checkIfWordMatches(String userSuggestion)
        {
            if (userSuggestion.Equals(currentWordToFind))
            {
                
                MessageBoxResult timeUP = MessageBox.Show("CORRECT! ", "AWESOME!");
                remainingObjects.Remove(currentWordToFind);
                resetScreenTimer();
                nextRoundOfMiniGame();

            }
        }


        /// <summary>
        /// Get the name of, and translate, the object in the analysis. 
        /// </summary>
        /// <param name="analysis"></param>
        private async void TranslateTextToForeignLanguage(String textToTranslate)
        {


            string toLanguageCode = languageCodesAndTitles[ToLanguageComboBox.SelectedValue.ToString()];

            string fromLanguageCode = "en";

            // Spell-check the source text if the source language is English
            if (fromLanguageCode == "en")
            {
                if (textToTranslate.StartsWith("-"))    // don't spell check in this case
                    textToTranslate = textToTranslate.Substring(1);
                else
                {
                    //textToTranslate = CorrectSpelling(textToTranslate);
                }
            }

            // Handle null operations: no text or same source/target languages
            if (textToTranslate == "")
            {
                DetectedObjectLabel.Content = "Error";

                return;

            } else if(fromLanguageCode == toLanguageCode)
            {
                TranslatedTextLabel.Content = textToTranslate;
            }




            // Send translation request
            string endpoint = string.Format(TEXT_TRANSLATION_API_ENDPOINT, "translate");
            string uri = string.Format(endpoint + "&from={0}&to={1}", fromLanguageCode, toLanguageCode);

            System.Object[] body = new System.Object[] { new { Text = textToTranslate } };
            var requestBody = JsonConvert.SerializeObject(body);

            using (var client = new HttpClient())
            using (var request = new HttpRequestMessage())
            {
                request.Method = HttpMethod.Post;
                request.RequestUri = new Uri(uri);
                request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                request.Headers.Add("Authorization", "Bearer " + authToken);
                request.Headers.Add("Ocp-Apim-Subscription-Region", "global");
                request.Headers.Add("X-ClientTraceId", Guid.NewGuid().ToString());

                var response = await client.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();
                Debug.WriteLine("Response: " + responseBody.ToString());

                var result = JsonConvert.DeserializeObject<List<Dictionary<string, List<Dictionary<string, string>>>>>(responseBody);
                var translation = result[0]["translations"][0]["text"];


                // Update the translation field
                TranslatedTextLabel.Content = translation;
            }
        }



        /// <summary>
        /// Translates the string to the foreign language, for the minigame. 
        /// </summary>
        /// <param name="textToTranslate"></param>
        private async void TranslateTextToForeignLanguageForMiniGameToFind(String textToTranslate)
        {


            string toLanguageCode = languageCodesAndTitles[ToLanguageComboBox.SelectedValue.ToString()];

            string fromLanguageCode = "en";

            // Spell-check the source text if the source language is English
            if (fromLanguageCode == "en")
            {
                if (textToTranslate.StartsWith("-"))    // don't spell check in this case
                    textToTranslate = textToTranslate.Substring(1);
                else
                {
                    //textToTranslate = CorrectSpelling(textToTranslate);
                }
            }

            // Handle null operations: no text or same source/target languages
            if (textToTranslate == "")
            {
                ObjectToFindLabel.Content = "Error";

                return;

            }
            else if (fromLanguageCode == toLanguageCode)
            {
                ObjectToFindLabel.Content = textToTranslate;
            }




            // Send translation request
            string endpoint = string.Format(TEXT_TRANSLATION_API_ENDPOINT, "translate");
            string uri = string.Format(endpoint + "&from={0}&to={1}", fromLanguageCode, toLanguageCode);

            System.Object[] body = new System.Object[] { new { Text = textToTranslate } };
            var requestBody = JsonConvert.SerializeObject(body);

            using (var client = new HttpClient())
            using (var request = new HttpRequestMessage())
            {
                request.Method = HttpMethod.Post;
                request.RequestUri = new Uri(uri);
                request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                request.Headers.Add("Authorization", "Bearer " + authToken);
                request.Headers.Add("Ocp-Apim-Subscription-Region", "global");
                request.Headers.Add("X-ClientTraceId", Guid.NewGuid().ToString());

                var response = await client.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();
                Debug.WriteLine("Response: " + responseBody.ToString());

                var result = JsonConvert.DeserializeObject<List<Dictionary<string, List<Dictionary<string, string>>>>>(responseBody);
                var translation = result[0]["translations"][0]["text"];


                // Update the translation field
                ObjectToFindLabel.Content = translation;
            }
        }


        private void DetectionButtonClick(object sender, RoutedEventArgs e)
        {
            runDetection();
        }

        private void runDetection()
        {
           
            string localPath = ImageFileLocation.Text;
            //creates an async chain of tasks, with the first one being AnalyzeLocalAsync.
            var info = AnalyzeLocalAsync(localPath);
            
            Task.WhenAll(info).Wait(5000);
        }


        /********************************************************************************************
         * IMAGE CAPTURE CODE
         **/

        public Collection<EncoderDevice> VideoDevices { get; set; }
        public Collection<EncoderDevice> AudioDevices { get; set; }

        private void startPreview()
        {

            try
            {
                VideoDevices = EncoderDevices.FindDevices(EncoderDeviceType.Video);
                AudioDevices = EncoderDevices.FindDevices(EncoderDeviceType.Audio);
                // Display webcam video
                WebcamViewer.VideoDevice = VideoDevices[1]; //contains a list of the video devices. Try changing the number if your's isn't working. This should be changed to pull from a list. 
                WebcamViewer.StartPreview();
            }
            catch (Microsoft.Expression.Encoder.SystemErrorException ex)
            {
                MessageBox.Show("Device is in use by another application");
            }
        }


        private void capturePhoto()
        {

            WebcamViewer.ImageDirectory = "X:\\BCIT Work\\CST Y2\\Term 1\\COMP 3951 Tech Pro\\Project\\GitHubRepo\\VisualLang\\TestImages";
            
            String imageFile = WebcamViewer.TakeSnapshot();
            ImageFileLocation.Text = imageFile;
            try
            {
                DetectedObjectLabel.Content = "Detecting object...";
                runDetection();
            } catch (Exception e)
            {
                DetectedObjectLabel.Content = "Error capturing photo.";
                Console.WriteLine("Something went wrong, but I caught it.");
            }
            
            
        }

        
        private void SnapshotBtn_Click(object sender, RoutedEventArgs e)
        {

            TranslatedTextLabel.Content = "Detecting and Translating...";
            capturePhoto();
            
        }


        /// <summary>
        /// The play minigame button was clicked. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PlayMGBtn_Click(object sender, RoutedEventArgs e)
        {
            startMiniGame();
        }



        /// <summary>
        /// Checks if the minigame has enough data to launch, and launches or displays an alert. 
        /// </summary>
        private void startMiniGame()
        {
            //Check if there are enough known items to play the game
            if (knownObjects.Count < miniGameKnownObjectLimit)
            {
                int diff = miniGameKnownObjectLimit - knownObjects.Count;
                //display alert.
                MessageBoxResult warning = MessageBox.Show("Please scan " + diff.ToString() + " more objects, then we can play!", "Almost!");

            }
            else
            {

                launchMiniGame();

            }
        }


        ArrayList remainingObjects;
        /// <summary>
        /// Starts the minigame. 
        /// </summary>
        private void launchMiniGame()
        {
            playingMiniGame = true;
            remainingObjects = knownObjects;
            nextRoundOfMiniGame();

        }


        private void nextRoundOfMiniGame()
        {

            if(remainingObjects.Count <= 0)
            {
                MessageBoxResult timeUP = MessageBox.Show("You've completed the game!", "Game over!");
            } else
            {
                //1. Choose a random word from the list of known objects
                Random randomNumGen = new Random();
                int randomWordIndex = randomNumGen.Next(0, remainingObjects.Count);
                currentWordToFind = (String)remainingObjects[randomWordIndex];

                //2. Get the translation of the word. 
                TranslateTextToForeignLanguageForMiniGameToFind(currentWordToFind);

                //3.Start the timer. 
                startMiniGameTimer(currentWordToFind);
            }
            

            
        }


        private void startMiniGameTimer(String objectToFind)
        {

            miniGameTimer.Elapsed += delegate { updateScreenTimer(objectToFind); };
            miniGameTimer.Start();

        }

        


        private void miniGameTimeUp(String objectToFind)
        {
            //display an alert
            MessageBoxResult timeUP = MessageBox.Show("Time up, the word was: \"" + objectToFind + "\"", "Time up!");
            resetScreenTimer();

        }

       


        private void updateScreenTimer(string objectToFind)
        {
            timeRemaining -= 1;
            if(timeRemaining <= 0)
            {
                this.Dispatcher.Invoke(() =>
                {
                   miniGameTimeUp(objectToFind);
                    miniGameTimer.Stop();

                });
            }
            
            this.Dispatcher.Invoke(() =>
            {
                CountDownTimer.Content = timeRemaining.ToString();
            });

        }


        private void resetScreenTimer()
        {
            miniGameTimer.Stop();
            timeRemaining = 30;
            CountDownTimer.Content = timeRemaining;
            
        }

    }

}
         



    