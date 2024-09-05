using Discord.WebSocket;
using Discord;
using OpenAI.Chat;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProjectOmega
{
    public class AdventureConversation
    {
        private enum Stat
        {
            CreateCharacter,
            Introduction,
            Play
        }

        public SocketThreadChannel ThreadChannel { get; private set; }
        public SocketUser User { get; private set; }

        private ChatClient client = new ChatClient("gpt-4o-mini", Environment.GetEnvironmentVariable(EnvironmentVariable.OPENAI_TOKEN));
        private List<IMessage> messages = new List<IMessage>();
        private Stat stat = Stat.CreateCharacter;
        private string characterDescription;

        private AdventureConversation()
        {
        }

        public static async Task<AdventureConversation> StartNewAdventure(SocketTextChannel channel, SocketUser user)
        {
            AdventureConversation adventureConversation = new AdventureConversation();

            await adventureConversation.Create(channel, user);
            await adventureConversation.Start();

            return adventureConversation;
        }

        private async Task Create(SocketTextChannel channel, SocketUser user)
        {
            ThreadChannel = await channel.CreateThreadAsync("A l'aventure", ThreadType.PrivateThread);
            User = user;

            await ThreadChannel.AddUserAsync(channel.Guild.GetUser(user.Id));
        }

        private async Task Start()
        {
            await ThreadChannel.SendMessageAsync("Bonjour aventurier et bienvenu dans cette nouvelle aventure.");
            await ThreadChannel.SendMessageAsync("Tout d'abord commençons par créer ton personnage. Dis moi qui désires-tu incarner.");
        }

        public async Task OnMessageReceived(SocketMessage message)
        {
            messages.Add(message);

            if (message.Author.Id == User.Id)
            {
                if (stat == Stat.CreateCharacter)
                {
                    await CreateCharacterConversation();
                }
                else if (stat == Stat.Play)
                {
                    await PlayConversation();
                }
            }
        }

        private async Task CreateCharacterConversation()
        {
            List<ChatMessage> chatMessages = new List<ChatMessage>();
            chatMessages.Add(new SystemChatMessage("Tu es maitre du jeu d'un univers médiévale fantastique. Tu dois aider le joueur à créer son personnage. Puis écrit un résumé de son personnage, son histoire, ses objectifs."));

            foreach (SocketMessage message in messages)
            {
                string userName = message.Author.Username;
                string messageContent = message.Content;

                foreach (SocketUser user in (message.Channel as SocketChannel).Users)
                {
                    messageContent = messageContent.Replace(user.Mention, user.Username);
                }

                if (message.Author.Id == User.Id)
                {
                    chatMessages.Add(new UserChatMessage(messageContent));
                }
                else
                {
                    chatMessages.Add(new AssistantChatMessage(messageContent));
                }
            }

            ChatCompletionOptions options = new ChatCompletionOptions();
            ChatTool tool = ChatTool.CreateFunctionTool("create_character", "Create a character for the game", BinaryData.FromString("""
                {
                  "type": "object",
                  "properties": {
                    "character_description": {
                      "type": "string",
                      "description": "The description of the character"
                    }
                  },
                  "required": [
                    "character_description"
                  ],
                  "additionalProperties": false
                }
                """));
            options.Tools.Add(tool);

            ChatCompletion respondChatCompletion = client.CompleteChat(chatMessages, options);

            if (respondChatCompletion.ToolCalls.Count > 0)
            {
                string functionArguments = respondChatCompletion.ToolCalls.Single().FunctionArguments;
                JObject functionArgumentsJson = (JObject)JsonConvert.DeserializeObject(functionArguments);
                characterDescription = functionArgumentsJson["character_description"].Value<string>();
                Console.WriteLine(characterDescription);
                await ThreadChannel.SendMessageAsync("Très bien, nous allons pouvoir commencer ton aventure.");
                stat = Stat.Introduction;

                await IntroductionConversation();
            }
            else
            {
                string respond = respondChatCompletion.Content[0].Text;
                await SendMessageAsyncSplit(respond);
            }
        }

        private async Task IntroductionConversation()
        {
            messages.Clear();

            List<ChatMessage> chatMessages = new List<ChatMessage>();
            chatMessages.Add(new SystemChatMessage("Tu es le maitre du jeu de l'univers médiévale fantastique. Tu dois créer l'introduction et lancer cette nouvelle aventure."));
            chatMessages.Add(new SystemChatMessage($"Le personnage du joueur : {characterDescription}"));

            ChatCompletion respondChatCompletion = client.CompleteChat(chatMessages);

            string respond = respondChatCompletion.Content[0].Text;
            await SendMessageAsyncSplit(respond);

            stat = Stat.Play;
        }

        private async Task PlayConversation()
        {
            List<ChatMessage> chatMessages = new List<ChatMessage>();
            chatMessages.Add(new SystemChatMessage("Tu es le maitre du jeu de l'univers médiévale fantastique."));
            chatMessages.Add(new SystemChatMessage($"Le personnage du joueur : {characterDescription}"));

            foreach (SocketMessage message in messages)
            {
                string userName = message.Author.Username;
                string messageContent = message.Content;

                foreach (SocketUser user in (message.Channel as SocketChannel).Users)
                {
                    messageContent = messageContent.Replace(user.Mention, user.Username);
                }

                if (message.Author.Id == User.Id)
                {
                    chatMessages.Add(new UserChatMessage(messageContent));
                }
                else
                {
                    chatMessages.Add(new AssistantChatMessage(messageContent));
                }
            }

            ChatCompletion respondChatCompletion = client.CompleteChat(chatMessages);

            string respond = respondChatCompletion.Content[0].Text;
            await SendMessageAsyncSplit(respond);
        }

        private async Task SendMessageAsyncSplit(string message)
        {
            string[] splitedMessage = message.Split(new string[] { "\n\n", "\r\n\r\n", "\r\r" }, StringSplitOptions.None);

            foreach (string splitedMessagePart in splitedMessage)
            {
                await ThreadChannel.SendMessageAsync(splitedMessagePart);
            }
        }
    }
}
