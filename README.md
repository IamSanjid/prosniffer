# PROSniffer
This is a custom packet sniffer(inspects all the communication between the server and the client) for Pokemon Revolution Online(https://pokemonrevolution.net/).

# Dependencies
[SharpCap](https://github.com/dotpcap/sharppcap)

# How to use?
Follow [SharpCap's](https://github.com/dotpcap/sharppcap) read me.
Easy setup: 
         Windows: Install [NpCap's](https://npcap.com/#download) dependencies.
         Linux: Install [libcap](https://www.tcpdump.org/manpages/pcap.3pcap.html) dependencies.
         MacOs: Check out [SharpCap's](https://github.com/dotpcap/sharppcap) guide, otherwise just install [WireShark](https://www.wireshark.org/docs/wsug_html_chunked/ChBuildInstallOSXInstall.html).

* Make sure you start to sniff the packets before logging in, otherwise won't be able to retain the proper RC4 state.
* Also make sure that no other application is connected through port 800 or 801 to some remote ipaddress other than PRO(Just close some other not really important applications?).
* You can continue writing on the standard input(console) while packets are being written on the standard output(console) to provide your next command.

Commands:
```
interfaces|i
   Desc: Shows all the eathernet/wireless interfaces on your machine.
sniff i=[interface index] <p|port=[ushort]; default is 800(Silver Server), provide 801 for Gold Server> <custom filter>
   Desc: Starts sniffing, if no argument is provided uses last provided arguments. 
         If you want to provide a custom filter like wireshark advance filters to detect PRO communication you can do that.
         Just provide it this way: sniff i=[index] cf="your filter".
filter|f
   Desc: You can provide custom Regex pattern to filter out packets.
pause|p|resume|r
   Desc: Pauses/Resumes from printing/logging packets.
clear|cls
   Desc: Clears the console screen, doesn't clear the internal packet log(which is used if you want to dump packets when quiting normally).
dump <file name> 
   Desc: Dumps all the packets inside the "{Default.DUMP_DIRECTORY}" folder.
exit|q
   Desc: Exits normally also dumps all the packets to a file if dump command was provided previously, check "{Default.DUMP_DIRECTORY}" folder.
h|help
   Desc: Prints out this message.
```
