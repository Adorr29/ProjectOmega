using Discord;
using Discord.Net;
using Discord.WebSocket;
using Newtonsoft.Json;

namespace ProjectOmega
{
    public class AdventureConversationManager
    {
        private DiscordSocketClient discordClient;
        private Dictionary<ulong, AdventureConversation> adventureDictionary = new Dictionary<ulong, AdventureConversation>();

        public AdventureConversationManager(DiscordSocketClient discordClient)
        {
            this.discordClient = discordClient;

            discordClient.MessageReceived += OnMessageReceived;
            discordClient.SlashCommandExecuted += OnSlashCommandExecuted;
            discordClient.ThreadDeleted += OnThreadDeleted;

            Task.Run(AddStartAdventureCommands);
        }

        private async Task OnMessageReceived(SocketMessage message)
        {
            SocketTextChannel threadChannel = (message.Channel as SocketThreadChannel);

            if (threadChannel == null)
                return;

            if (adventureDictionary.TryGetValue(message.Channel.Id, out AdventureConversation adventure))
            {
                if (message.Author.Id == adventure.User.Id || message.Author.Id == discordClient.CurrentUser.Id)
                {
                    await adventure.OnMessageReceived(message);
                }
                else
                {
                    Console.Error.WriteLine($"{message.Author.Username} ({message.Author.Id}) is pusting in channel {adventure.ThreadChannel.Id}");
                }
            }
        }

        private async Task OnSlashCommandExecuted(SocketSlashCommand command)
        {
            if (command.CommandName == "start-adventure")
            {
                await StartAdventure(command);
            }
        }

        private async Task OnThreadDeleted(Cacheable<SocketThreadChannel, ulong> cacheable)
        {
            adventureDictionary.Remove(cacheable.Id);
        }

        private async Task AddStartAdventureCommands()
        {
            foreach (SocketGuild guild in discordClient.Guilds)
            {
                SlashCommandBuilder guildCommand = new SlashCommandBuilder();
                guildCommand.WithName("start-adventure");
                guildCommand.WithDescription("Commencer une nouvelle aventure");

                try
                {
                    await guild.CreateApplicationCommandAsync(guildCommand.Build());
                }
                catch (ApplicationCommandException exception)
                {
                    // If our command was invalid, we should catch an ApplicationCommandException. This exception contains the path of the error as well as the error message. You can serialize the Error field in the exception to get a visual of where your error is.
                    var json = JsonConvert.SerializeObject(exception.Errors, Formatting.Indented);

                    // You can send this error somewhere or just print it to the console, for this example we're just going to print it.
                    Console.WriteLine(json);
                }
            }
        }

        private async Task StartAdventure(SocketSlashCommand command)
        {
            await command.RespondAsync("Bonne aventure !", ephemeral: true);

            SocketTextChannel channel = command.Channel as SocketTextChannel;
            SocketUser user = command.User;

            AdventureConversation adventure = await AdventureConversation.StartNewAdventure(channel, user);
            adventureDictionary.Add(adventure.ThreadChannel.Id, adventure);
        }
    }
}
