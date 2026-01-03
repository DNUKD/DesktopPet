using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Security.Authentication;


namespace DesktopPet
{
    enum FallState
    {
        Start,
        Flip,
        Falling,
        Impact,
        Recover,
        Done
    }

    public partial class PetWindow : Window
    {

        // Animáció Frame Listák
        List<BitmapImage> fallFrames = new();
        List<BitmapImage> walkRightFrames = new();
        List<BitmapImage> sitFrames = new();
        List<BitmapImage> cloudFrames = new();
        List<BitmapImage> windowFrames = new();

        // Felhő Animáció
        int cloudFrameIndex = 0;
        int cloudFrameTimer = 0; // frame váltó időzítő
        const int CloudFrameDelay = 8; // frame váltási késleltetés

        // UI Window Animáció
        int windowFrameIndex = 0;
        int windowFrameTimer = 0;
        bool isWindowOpening = false;
        const int WindowFrameDelay = 6;

        // Karakter Állapot
        bool isSitting = false;
        bool isWalking = false;
        bool walkStarted = false;

        // Ülés Animáció
        int sitFrameIndex = 0;
        int sitFrameTimer = 0;
        const int SitFrameDelay = 10;

        // Séta Animáció
        int walkFrameIndex = 0;

        // Esés Állapot 
        FallState state = FallState.Start;
        int stateTimer = 0;
        int frameIndex = 0;

        // Mozgás / Fizika / Pozíciók
        double screenWidth = SystemParameters.WorkArea.Width;
        double petWidth = 150;
        double petX;
        double petY = 10;
        double velocity = 0;

        double gravity = 2.0;
        double maxFallSpeed = 36;
        double groundY;

        // WPF komponens szálbiztos időzítést biztosít UI szálon
        DispatcherTimer timer;


        string petName = "Lumi";

        bool welcomeShown = false;

        private static readonly HttpClient httpClient =
        new HttpClient(new HttpClientHandler
        {
            SslProtocols = SslProtocols.Tls12
        });


        private const string AI_ENDPOINT = "https://desktoppet-ai.h-ilona27.workers.dev";


        //Chat memória 
        List<(string role, string content)> chatMemory = new();

        const int MaxMemoryMessages = 6;

        public PetWindow()
        {


            InitializeComponent();

            // Kezdeti pozíciók és framek betöltése
            petX = (screenWidth - petWidth) / 2;
            groundY = SystemParameters.WorkArea.Height - PetImage.Height;
            LoadFrames();

            // XML elemek beállítása
            Canvas.SetLeft(PetImage, petX);
            Canvas.SetTop(PetImage, petY);
            PetImage.Source = fallFrames[0];

            // Frissítő ciklus setup
            timer = new DispatcherTimer
            {
                // 1000ms / 16ms = 60fps képernyőfrissítési sebesség
                Interval = TimeSpan.FromMilliseconds(16)
            };
            timer.Tick += Update; //  while(running) { Thread.Sleep(16ms); Update(); }
            timer.Start();

        }

        void LoadFrames()
        {
            for (int i = 1; i <= 7; i++)
                fallFrames.Add(new BitmapImage(
                    new Uri($"/Assets/Animations/Fall/fall_0{i}.png", UriKind.Relative)));

            for (int i = 1; i <= 3; i++)
                walkRightFrames.Add(new BitmapImage(
                    new Uri($"/Assets/Animations/Walk/Right/walk_right_0{i}.png", UriKind.Relative)));

            for (int i = 1; i <= 4; i++)
                sitFrames.Add(new BitmapImage(
                    new Uri($"pack://application:,,,/Assets/Animations/Sit/sit_0{i}.png")));

            for (int i = 1; i <= 5; i++)
                cloudFrames.Add(new BitmapImage(
                    new Uri($"pack://application:,,,/Assets/Animations/UI_elements/Trigger/felho_0{i}.png")));

            for (int i = 1; i <= 4; i++)
                windowFrames.Add(new BitmapImage(
                    new Uri($"pack://application:,,,/Assets/Animations/UI_elements/Window/window_0{i}.png")));
        }


        // az Update végignézi az összes állapotot és frissíti a megfelelő frame-eket és pozíciókat
        void Update(object? sender, EventArgs e)
        {

            if (isWindowOpening)
                UpdateWindowAnimation();


            if (CloudImage.Visibility == Visibility.Visible)
                UpdateCloudAnimation();


            if (isSitting)
            {
                // Timer számlál, minden update ciklusban növekszik
                sitFrameTimer++;

                // ha eléri a delay értéket akkor váltunk frame-et - a delay most egy konstans érték
                if (sitFrameTimer >= SitFrameDelay)
                {
                    sitFrameTimer = 0;
                    sitFrameIndex++;

                    if (sitFrameIndex >= sitFrames.Count)
                    {
                        sitFrameIndex = sitFrames.Count - 1;
                        isSitting = false;
                    }
                }

                PetImage.Source = sitFrames[sitFrameIndex];
                return;
            }

            if (isWalking)
            {

                PetImage.Source = walkRightFrames[walkFrameIndex];

                // ismételjük a sétáló frame-eket (loop a modulo segítségével)
                walkFrameIndex = (walkFrameIndex + 1) % walkRightFrames.Count;

                petX += 4.2;

                // Megállítjuk a képernyő szélén 
                double maxX = SystemParameters.WorkArea.Width
                              - PetImage.Width
                              - (413 + 40);

                if (petX >= maxX)
                {
                    petX = maxX;
                    isWalking = false;
                    StartSit();
                }

                Canvas.SetLeft(PetImage, petX);
                return;
            }

            switch (state)
            {
                // a velocity az a zuhanási sebesség
                // petY a pet pozíciója a képernyőn

                // a velocity-t növeljük a gravity-vel, és hozzáadjuk a petY-hoz, amikor eléri a groundY-t megállítjuk az esést
                // PetImage.Source -> megfelelő frame-et állítjuk be esés állapotának megfelelően
                // stateTimer egy számláló mennyi időt töltöttünk adott állapotban

                case FallState.Start:
                    velocity += gravity * 0.3;
                    petY += velocity;
                    PetImage.Source = fallFrames[0];
                    if (++stateTimer > 20) { state = FallState.Flip; stateTimer = 0; }
                    break;

                case FallState.Flip:
                    velocity += gravity * 0.6;
                    petY += velocity;
                    PetImage.Source = fallFrames[1];
                    if (++stateTimer > 5) { state = FallState.Falling; stateTimer = 0; }
                    break;

                case FallState.Falling:
                    velocity = Math.Min(velocity + gravity, maxFallSpeed);
                    petY += velocity;
                    PetImage.Source = fallFrames[2];
                    if (petY >= groundY) { petY = groundY; state = FallState.Impact; }
                    break;

                case FallState.Impact:
                    PetImage.Source = fallFrames[3];
                    if (++stateTimer > 6) { state = FallState.Recover; frameIndex = 4; stateTimer = 0; }
                    break;

                case FallState.Recover:
                    PetImage.Source = fallFrames[frameIndex];
                    if (++stateTimer > 12 && ++frameIndex > 6)
                        state = FallState.Done;
                    break;

                case FallState.Done:
                    if (!walkStarted)
                    {
                        walkStarted = true;
                        StartWalkRight();
                    }
                    break;
            }

            Canvas.SetTop(PetImage, petY);
        }

        // Metódusok az állapotok indításához
        void StartWalkRight()
        {
            isWalking = true;
            walkFrameIndex = 0;
        }

        void StartSit()
        {
            isSitting = true;
            sitFrameIndex = 0;
            ShowCloud();
        }

        void ShowCloud()
        {
            CloudImage.Source = cloudFrames[0];
            CloudImage.Visibility = Visibility.Visible;
            Canvas.SetLeft(CloudImage, petX + PetImage.Width - 50);
            Canvas.SetTop(CloudImage, petY - 70);
        }

        void UpdateCloudAnimation()
        {
            if (++cloudFrameTimer >= CloudFrameDelay)
            {
                cloudFrameTimer = 0;
                cloudFrameIndex = (cloudFrameIndex + 1) % cloudFrames.Count;
                CloudImage.Source = cloudFrames[cloudFrameIndex];
            }
        }
        void UpdateWindowAnimation()
        {
            if (!isWindowOpening)
                return;

            windowFrameTimer++;

            if (windowFrameTimer >= WindowFrameDelay)
            {
                windowFrameTimer = 0;
                windowFrameIndex++;

                if (windowFrameIndex >= windowFrames.Count)
                {
                    windowFrameIndex = windowFrames.Count - 1;
                    isWindowOpening = false;

                    UIContent.Visibility = Visibility.Visible;
                    ChatInput.Visibility = Visibility.Visible;
                    SendButton.Visibility = Visibility.Visible;
                    ChatInput.Focus();
                    ShowWelcomeMessage();

                }

                UIWindowImage.Source = windowFrames[windowFrameIndex];
            }
        }

        // Eseménykezelők

        // XAML-ben a CloudImage elemen bevanállítva a MouseLeftButtonDown esemény 
        private void Cloud_Click(object sender, MouseButtonEventArgs e)
        {
            CloudImage.Visibility = Visibility.Collapsed;
            UIWindow.Visibility = Visibility.Visible;

            windowFrameIndex = 0;
            windowFrameTimer = 0;
            isWindowOpening = true;

            UIWindowImage.Source = windowFrames[0];

            double uiX = petX + PetImage.Width - 80;
            double uiY = petY - 250;

            // több Canvasunk van: itt most a UIWindow-t pozicionáljuk
            Canvas.SetLeft(UIWindow, uiX);
            Canvas.SetTop(UIWindow, uiY);
        }

        private void ChatInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendMessage();
                e.Handled = true;
            }
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        // asyn void - eseménykezelőként használva
        // a UI szál továbbra is reagálhat az eseményekre, miközben a metódus várakozik az OpenAI API válaszára
        async void SendMessage()
        {
            if (welcomeShown)
            {
                ChatText.Text = "";
                welcomeShown = false;
            }

            SendButton.IsEnabled = false;
            ChatInput.IsEnabled = false;

            string text = ChatInput.Text.Trim();
            if (string.IsNullOrEmpty(text))
            {
                SendButton.IsEnabled = true;
                ChatInput.IsEnabled = true;
                return;
            }

            chatMemory.Add(("user", text));
            TrimMemory();

            ChatText.Text += $" {text}\n\n";
            ChatInput.Clear();
            ChatScroll.ScrollToEnd();

            ChatText.Text += $"{petName}: gondolkodik... 🤔\n\n";
            ChatScroll.ScrollToEnd();

            try
            {
                ChatText.Text = ChatText.Text.Replace($"{petName}: gondolkodik... 🤔\n\n", "");

                string reply = await AskAIAsync(text);

                chatMemory.Add(("assistant", reply));
                TrimMemory();

                await TypeTextAsync($"🐾 {petName}: " + reply);

            }
            catch (Exception ex)
            {
                // ha bármi hiba van (net, quota, timeout, stb.)
                //ChatText.Text = ChatText.Text.Replace($"{petName}: gondolkodik... 🤔\n\n", "");
                //ChatText.Text += $"{petName}: most elfáradtam egy kicsit 😴 próbáld meg később!\n\n";

                ChatText.Text += $"{petName}: HIBA TÖRTÉNT\n{ex.GetType().Name}\n{ex.Message}\n\n";
            }
            finally
            {
                SendButton.IsEnabled = true;
                ChatInput.IsEnabled = true;
                ChatInput.Focus();
                ChatScroll.ScrollToEnd();
            }
        }

        async Task TypeTextAsync(string text)
        {
            foreach (char c in text)
            {
                ChatText.Text += c;
                ChatScroll.ScrollToEnd();
                await Task.Delay(25);
            }

            ChatText.Text += "\n\n";
        }

        void ShowWelcomeMessage()
        {
            if (welcomeShown) return;

            ChatText.Text += $"{petName}: Szia! 🙂 Írj nyugodtan.\n\n";
            welcomeShown = true;
        }

        // Kilépés az alkalmazásból 
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Application.Current.Shutdown();
        }

        // XAML-ben a Exit gomb eseménykezelője
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        // Chat memória korlátozása konstans alapján
        void TrimMemory()
        {
            while (chatMemory.Count > MaxMemoryMessages)
                chatMemory.RemoveAt(0);
        }

        private async Task<string> AskAIAsync(string userMessage)
        {
            var messages = new List<object>();

            // system prompt
            messages.Add(new
            {
                role = "system",
                content = "Segítőkész asztali asszisztens vagy. Magyarul válaszolj."
            });

            // chat memória
            foreach (var msg in chatMemory)
            {
                messages.Add(new
                {
                    role = msg.role,
                    content = msg.content
                });
            }

            var payload = new
            {
                model = "gpt-4o-mini",
                messages = messages
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(AI_ENDPOINT, content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);

            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()!;
        }


    }
}
