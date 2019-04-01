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




/// <summary>
/// References:
/// *********************************************************************************
/// Title: WPF-Webcam-control
/// Author: Meshack Musundi
/// Date: September 25, 2017
/// Code Version: 3.3.1
/// Type: Nuget Package
/// Availability: https://www.codeproject.com/Articles/285964/WPF-Webcam-Control
/// *********************************************************************************
/// Title: Microsoft Translator Text API
/// Author: Microsoft
/// Date: March 28, 2018
/// Code Version: 3.0
/// Type: API
/// Availability: https://docs.microsoft.com/en-us/azure/cognitive-services/translator/
/// *********************************************************************************
/// Title: Microsoft Computer Vision API
/// Author: Microsoft
/// Date: 2017
/// Code Version: 2.0
/// Type: API
/// Availability: https://azure.microsoft.com/en-ca/services/cognitive-services/computer-vision/
/// *********************************************************************************
/// Title: Bing Spell Check API
/// Author: Microsoft
/// Date: June 20, 2016
/// Code Version: 7.0
/// Type: API
/// Availability: https://azure.microsoft.com/en-ca/services/cognitive-services/spell-check/
/// *********************************************************************************
/// </summary>
namespace MSTranslatorTextDemo
{

    public partial class MainWindow : Window
    {
        // This sample uses the Cognitive Services subscription key for all services. To learn more about
        // authentication options, see: https://docs.microsoft.com/azure/cognitive-services/authentication.
        const string COGNITIVE_SERVICES_KEY = "ef0b37aefa9748fdae669e201b8ecbaa"; //Eamonn's key to Microsoft Cognitive Services
        const string SPELL_CHECK_KEY = "11b50d922c7b443d962e7631f983feb3"; //Eamonn's key to the microsoft spellcheck service. 
        const string OBJECT_RECOGNIZER_KEY = "6926aeac904449bab4a529027e187fe8"; //Skyler's key to the object recognition engine. 

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
        private static int tokenTimeout = 8 * 60 * 1000; //8 minutes, when a new token has to be obtained. 

        String foreignLanguageText; //gets populated by the translateTextIntoLanguage() function. 
        String unknownObjectString = "Unknown Object"; //

        //variables associated with the minigame. 
        ArrayList knownObjects; //an arraylist to keep known objects in. 
        int miniGameKnownObjectLimit = 3; //must have this many items recognized in order to play. 
        System.Timers.Timer miniGameTimer = new System.Timers.Timer(1000); //a timer to update the time remaining on the main screen. 
        int fullTimeRemaining = 30; //the amount of time the user gets to find the object. 
        int timeRemaining = 30; //the number of seconds remaining the user has to find the object. 
        String currentWordToFind = ""; //the current word to find
        static Random randomNumGen = new Random(); //random number generator, used in picking random items in a list. 
        bool playingMiniGame = false; //whether the minigame is running or not, used to adjust actions within object recognition and translation. 
        int wrongGuesses = 0; //the number of total guesses the user made in the game
        int correctGuesses = 0; //the number of correct guesses the user made in the game


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


        /// <summary>
        /// Prepare the visual aspects of the screen, hiding and setting labels, etc. 
        /// </summary>
        private void styleGUI()
        {
            TranslatedTextLabel.Content = "Translated Text will Appear Here.";
            DetectedObjectLabel.Content = "Detected Object Name will Appear Here";
            CountDownTimerLabel.Visibility = Visibility.Hidden;
            ObjectToFindLabel.Visibility = Visibility.Hidden;
            FindLabel.Visibility = Visibility.Hidden;
            CountDownTimerLabel.Visibility = Visibility.Hidden;
            WordsLeftCount.Visibility = Visibility.Hidden;
            WordsLeftLabel.Visibility = Visibility.Hidden;
            ScoreValue.Visibility = Visibility.Hidden;
            ScoreNameLabel.Visibility = Visibility.Hidden;

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

            if (playingMiniGame)
            {
                miniGameTimer.Stop();
            }


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

                //delete the file
                File.Delete(imagePath);
 
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


        /// <summary>
        /// Checks if the word provided by the user matches the word requested by the game. 
        /// </summary>
        /// <param name="userSuggestion">The name of the object provided by the user. </param>
        private void checkIfWordMatches(String userSuggestion)
        {

            

            if (userSuggestion.ToLower().Equals(currentWordToFind.ToLower()))
            {
                
                MessageBoxResult match = MessageBox.Show("CORRECT! ", "AWESOME!");
                Console.WriteLine("Word matches: " + currentWordToFind);
                remainingObjects.Remove(currentWordToFind);
                foreach (String word in remainingObjects)
                {
                    Console.WriteLine("Remaining item: " + word);
                }

                correctGuesses++;
                resetScreenTimer();
                nextRoundOfMiniGame();

            } else
            {
                wrongGuesses++;
                MessageBoxResult timeUP = MessageBox.Show("Try Again! ", "Nope!");
                miniGameTimer.Start();

            }

            ScoreValue.Content = correctGuesses.ToString() + " / " + wrongGuesses.ToString();
            

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

        /// <summary>
        /// The user clicks the "detect and translate" button. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DetectionButtonClick(object sender, RoutedEventArgs e)
        {
            runDetection();
        }

        /// <summary>
        /// Starts object detection. 
        /// </summary>
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


        /// <summary>
        /// STarts the video capture devices and shows the feed on the screen.  
        /// </summary>
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


        /// <summary>
        /// Caputres a photo. 
        /// </summary>
        private void capturePhoto()
        {

            WebcamViewer.ImageDirectory = "X:\\BCIT Work\\CST Y2\\Term 1\\COMP 3951 Tech Pro\\Project\\GitHubRepo\\VisualLang\\TestImages";
            
            String imageFile = WebcamViewer.TakeSnapshot();
            ImageFileLocation.Text = imageFile;
            try
            {
                DetectedObjectLabel.Content = "Detecting object...";
                Console.WriteLine("Detecting object...");
                runDetection();
            } catch (Exception e)
            {
                DetectedObjectLabel.Content = "Error capturing photo.";
                Console.WriteLine("Something went wrong, but I caught it.");
            }
            
            
        }

        
        /// <summary>
        /// handles clicking the snapshot button. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SnapshotBtn_Click(object sender, RoutedEventArgs e)
        {

            TranslatedTextLabel.Content = "Detecting and Translating...";
            Console.WriteLine("Detecting and translating");
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
            knownObjects.Add("Cat");
            knownObjects.Add("Dog");
            knownObjects.Add("Bird");
            knownObjects.Add("fish");
            knownObjects.Add("car");

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
            toggleMiniGameLayout();
            remainingObjects = copyArrayListToArrayList(knownObjects);
            nextRoundOfMiniGame();

        }


        /// <summary>
        /// Copies a given arraylist to another, so that they don't end up as references. 
        /// </summary>
        /// <param name="fromList"></param>
        /// <returns></returns>
        private ArrayList copyArrayListToArrayList(ArrayList fromList)
        {

            ArrayList resultList = new ArrayList();

            foreach(object item in fromList)
            {
                resultList.Add(item);
            }

            return resultList;
        }



        /// <summary>
        /// Toggles minigame mode, showing or hiding the necessary elements. 
        /// </summary>
        private void toggleMiniGameLayout()
        {
           
            playingMiniGame = !playingMiniGame;
            correctGuesses = 0;
            wrongGuesses = 0;

            if (playingMiniGame)
            {
                CountDownTimerLabel.Visibility = Visibility.Visible;
                ObjectToFindLabel.Visibility = Visibility.Visible;
                FindLabel.Visibility = Visibility.Visible;
                PlayMGBtn.Content = "Stop MiniGame";
                WordsLeftCount.Visibility = Visibility.Visible;
                WordsLeftLabel.Visibility = Visibility.Visible;
                ScoreValue.Visibility = Visibility.Visible;
                ScoreNameLabel.Visibility = Visibility.Visible;
              

            } else
            {
                CountDownTimerLabel.Visibility = Visibility.Hidden;
                ObjectToFindLabel.Visibility = Visibility.Hidden;
                FindLabel.Visibility = Visibility.Hidden;
                CountDownTimerLabel.Visibility = Visibility.Hidden;
                miniGameTimer.Dispose();
                timeRemaining = fullTimeRemaining;
                PlayMGBtn.Content = "Play MiniGame";
                WordsLeftCount.Visibility = Visibility.Hidden;
                WordsLeftLabel.Visibility = Visibility.Hidden;
                ScoreValue.Visibility = Visibility.Hidden;
                ScoreNameLabel.Visibility = Visibility.Hidden;
  
            }

        }

        /// <summary>
        /// Starts the next round of the minigame. 
        /// </summary>
        private void nextRoundOfMiniGame()
        {
            
            foreach (String item in remainingObjects){
                Console.WriteLine(item);
            };

            

            stopTimer();
            miniGameTimer = new System.Timers.Timer(1000);
            if(remainingObjects.Count <= 0)
            {
                MessageBoxResult gameOver = MessageBox.Show("You've completed the game! Final Score: " + correctGuesses + " right guesses / " + wrongGuesses + " wrong guesses.", "Game over!");
                toggleMiniGameLayout();
                stopTimer();
            } else
            {
                //1. Choose a random word from the list of known objects
                
                int randomWordIndex = randomNumGen.Next(0, remainingObjects.Count);
                currentWordToFind = (String)remainingObjects[randomWordIndex];

                //2. Get the translation of the word. 
                TranslateTextToForeignLanguageForMiniGameToFind(currentWordToFind);

                //3.Start the timer. 
                startMiniGameTimer(currentWordToFind);

                //4. Display the content. 
                WordsLeftCount.Content = remainingObjects.Count;

            }

        }

        /// <summary>
        /// Starts the minigame timer for the given object. 
        /// </summary>
        /// <param name="objectToFind"></param>
        private void startMiniGameTimer(String objectToFind)
        {
            miniGameTimer.Enabled = true;
            miniGameTimer.Interval = 1000;
            miniGameTimer.Elapsed += delegate { updateScreenTimer(objectToFind); };
            miniGameTimer.Start();
            

        }

        

        /// <summary>
        /// Called when the timer runs out. Displays a message. 
        /// </summary>
        /// <param name="objectToFind"></param>
        private void miniGameTimeUp(String objectToFind)
        {
            //display an alert
            MessageBoxResult timeUP = MessageBox.Show("Time up, the word was: \"" + objectToFind + "\"", "Time up!");

        }

       

        /// <summary>
        /// Updates the label showing how much time is left on the screen and checks if time is up. 
        /// </summary>
        /// <param name="objectToFind"></param>
        private void updateScreenTimer(string objectToFind)
        {
            timeRemaining -= 1;
            if(timeRemaining <= 0)
            {
                this.Dispatcher.Invoke(() =>
                {
                    stopTimer();
                    miniGameTimeUp(objectToFind);
                    resetScreenTimer();
                    nextRoundOfMiniGame();

                });
            }
            
            this.Dispatcher.Invoke(() =>
            {
                CountDownTimerLabel.Content = timeRemaining.ToString();
            });

        }

        /// <summary>
        /// Stops the timer and resets it so we don't end up with multiple instances. 
        /// </summary>
        private void stopTimer()
        {
            miniGameTimer.Stop();
            miniGameTimer.Enabled = false;
            miniGameTimer.Dispose();
        }

        /// <summary>
        /// Resets the label showing how much time the user has left. 
        /// </summary>
        private void resetScreenTimer()
        {
            timeRemaining = fullTimeRemaining;
            CountDownTimerLabel.Content = timeRemaining;
        }



        /// <summary>
        /// The user clicked the About & privacy policy button. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ViewAbout(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("About clicked");
            WPFTranslationApp.AboutScreen aboutScreen = new WPFTranslationApp.AboutScreen();
            aboutScreen.Show();
        }

    }

}
         



    