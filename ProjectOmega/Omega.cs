using Discord;
using Discord.WebSocket;
using OpenAI.Assistants;
using OpenAI.Chat;
using System;
using System.Text;
using System.Timers;

namespace ProjectOmega
{
    public static class EnvironmentVariable
    {
        public const string DISCORD_TOKEN = nameof(DISCORD_TOKEN);
        public const string OPENAI_TOKEN = nameof(OPENAI_TOKEN);
    }

    public class Conversation
    {
        private SocketUser omegaUser;

        private ChatClient client;
        public List<SocketMessage> messages = new List<SocketMessage>();
        private System.Timers.Timer timer;

        private static string getRespondPrompt = "Tu es un assistant dans une conversation de groupe. Tu es connu sous le nom d'Oméga. T'on rôle est d'aider les personnes de ce groupe. Tu ne dois pas répondre si ce n'est pas nécessaire.";

        public Conversation(SocketUser omegaUser)
        {
            this.omegaUser = omegaUser;

            client = new ChatClient("gpt-4o-mini", Environment.GetEnvironmentVariable(EnvironmentVariable.OPENAI_TOKEN));

            timer = new System.Timers.Timer(TimeSpan.FromSeconds(1));
            timer.AutoReset = false;
            timer.Elapsed += Timer_Elapsed;
        }

        public bool IsOmega(SocketUser user)
        {
            return user.Id == omegaUser.Id;
        }

        public void RestartTimer()
        {
            timer.Stop();
            timer.Start();
        }

        private void Timer_Elapsed(object? sender, ElapsedEventArgs args)
        {
            messages.RemoveAll(m => m.CreatedAt < DateTime.Now.AddDays(-1));

            string respond = GetRespond();
            bool respondValid = ValidateRespond(respond);

            if (respondValid)
            {
                messages[0].Channel.SendMessageAsync(respond);
            }
        }

        private string GetRespond()
        {
            List<ChatMessage> chatMessages = new List<ChatMessage>();
            chatMessages.Add(new SystemChatMessage(getRespondPrompt));

            foreach (SocketMessage message in messages)
            {
                string userName = message.Author.Username;
                string messageContent = message.Content;

                foreach (SocketUser user in (message.Channel as SocketChannel).Users)
                {
                    messageContent = messageContent.Replace(user.Mention, user.Username);
                }

                if (IsOmega(message.Author))
                {
                    chatMessages.Add(new AssistantChatMessage(messageContent));
                }
                else
                {
                    chatMessages.Add(new UserChatMessage($"{userName} : {messageContent}"));
                }
            }

            ChatCompletion respondChatCompletion = client.CompleteChat(chatMessages);
            
            return respondChatCompletion.Content[0].Text;
        }

        private bool ValidateRespond(string respond)
        {
            List<ChatMessage> chatMessages = new List<ChatMessage>();
            chatMessages.Add(new SystemChatMessage("Ton rôle est de vérifier la pertinence de la réponse de l'assistant ainsi que de déterminer s'il aurait dû répondre ou non. Répond par oui ou par non."));

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Voilà la conversation :");

            foreach (SocketMessage message in messages)
            {
                string userName = message.Author.Username;
                string messageContent = message.Content;

                foreach (SocketUser user in (message.Channel as SocketChannel).Users)
                {
                    messageContent = messageContent.Replace(user.Mention, user.Username);
                }

                if (message.Author.Id == omegaUser.Id)
                {
                    sb.AppendLine($"Oméga : {messageContent}");
                }
                else
                {
                    sb.AppendLine($"{userName} : {messageContent}");
                }
            }

            sb.AppendLine();

            sb.AppendLine("Voilà les instructions de l'assistant :");
            sb.AppendLine(getRespondPrompt);

            sb.AppendLine();

            sb.AppendLine("Et voilà la reponse de l'assistant :");
            sb.AppendLine(respond);

            chatMessages.Add(new UserChatMessage(sb.ToString()));

            ChatCompletion validateRespondChatCompletion = client.CompleteChat(chatMessages);

            return string.Equals(validateRespondChatCompletion.Content[0].Text, "oui", StringComparison.OrdinalIgnoreCase);
        }
    }

    public class Omega()
    {
        private DiscordSocketClient client = new DiscordSocketClient(new DiscordSocketConfig() { 
            GatewayIntents = GatewayIntents.All
        });

        Dictionary<ulong, Conversation> conversationDictionary = new Dictionary<ulong, Conversation>();

        public async Task Start()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvironmentVariable.DISCORD_TOKEN)))
            {
                throw new Exception($"Environment variable \"{nameof(EnvironmentVariable.DISCORD_TOKEN)}\" can't be null or empty");
            }

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvironmentVariable.OPENAI_TOKEN)))
            {
                throw new Exception($"Environment variable \"{nameof(EnvironmentVariable.OPENAI_TOKEN)}\" can't be null or empty");
            }

            Console.WriteLine("Start Oméga");

            await client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable(EnvironmentVariable.DISCORD_TOKEN));

            client.MessageReceived += OnMessageReceived;
            client.Log += OnLog;

            await client.StartAsync();
            await Task.Delay(-1);
        }

        private Task OnLog(LogMessage message)
        {
            Console.WriteLine(message);
            return Task.CompletedTask;
        }

        private async Task OnMessageReceived(SocketMessage message)
        {
            AddMessageToConversation(message);
        }

        private void AddMessageToConversation(SocketMessage newMessage)
        {
            if (!conversationDictionary.TryGetValue(newMessage.Channel.Id, out Conversation conversation))
            {
                conversation = new Conversation(client.CurrentUser);
                conversationDictionary.Add(newMessage.Channel.Id, conversation);
            }

            conversation.messages.Add(newMessage);

            if (!conversation.IsOmega(newMessage.Author))
            {
                conversation.RestartTimer();
            }
        }
    }
}