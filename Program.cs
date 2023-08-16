/*
SerialHelper is for use with the L3 KeyAT-4 RS-232 to PS/2 adapter.

While tested with the Commander 16 system, it can also be used with other systems that have a PS/2 keyboard connection.
I tried using ZOC to do this, but found that a custom terminal program is necessary to throttle the key sequences appropriately.

~I   enabled interactive mode

Refer to the example input file on how to script some sequences.
Just run the program to see the command line arguments.

        Reminder/example for X16:
        ~+29~+56~+83   CTRL+ALT+DEL


 */
using System;
using System.IO.Ports;
using System.Threading;

public class SerialHelper
{
    static int SLEEP_DELAY = 42;

    static bool _continue;
    static SerialPort _serialPort = null;

    public static void sendCR()
    {
        _serialPort.Write("" + (char)(0x0D));
    }

    public static void sendMessageLine(string messageLine)
    {
        while (true)
        {
            // In case it is a DOS/Windows type of text file, go ahead and remove the CR if it is present
            // No real reason that there should be more than 1 of them, if any, but loop just in case
            // of some funny hex edited weird file.
            int i = messageLine.IndexOf('\r');
            if (i >= 0)
            {
                messageLine = messageLine.Remove(i, 1);
            }
            else
            {
                break;
            }
        }

        if (messageLine.Length > 0)  // as long as we have some content...
        {
            if (messageLine[0] == ';')
            {
                // ignore the entire line - assume it is a comment
            }
            else
            {
                // Loop and send each character of the string...
                for (int i = 0; i < messageLine.Length; ++i)
                {
                    _serialPort.Write("" + (char)messageLine[i]);
                    Thread.Sleep(SLEEP_DELAY);  // We need a small delay or we'll fill up the keyboard buffer too quickly
                    // For X16 this was found as the shortest delay across PS/2 keyboard interface without losing characters
                    // 1000/40 = 25 characters per second, giving an effective "baud" rate of 250 (10 bits per character, including the start/stop bits)
                }
            }
        }

        // Certain "special commands" of the L3 KeyAT should have a CR sent after them...
        int n = messageLine.IndexOf("~I");  // "immediate mode"
        if (n >= 0)
        {
            sendCR();
        }
        else
        {
            n = messageLine.IndexOf("~Z");  // "delay N seconds"
            if (n >= 0)
            {
                sendCR();
                int z_value = int.Parse(messageLine.Substring(n + 2));  // +2 to skip the "~Z" and get any remaining content, which should just be a number
                if (z_value > 0)
                {
                    // We need our own program to match the specified delay time, so that we don't "send ahead" while
                    // the L3 KeyAT is sleeping... I do +1 to try to make sure we've slept at least as long as the KeyAT device
                    // *1000 to convert seconds to milliseconds
                    Thread.Sleep((z_value + 1) * 1000);
                }
            }
        }
    }

    public static void showCommandArguments()
    {
        Console.WriteLine("<comX> <baud> <dataBits> <parity> <stopBits> <handshake> [file] [REPEAT]");
        Console.WriteLine("example: com3 9600 8 None 1 None sample.txt REPEAT");

        Console.WriteLine("Available Ports:");
        foreach (string s in SerialPort.GetPortNames())
        {
            Console.WriteLine("   {0}", s);
        }
        
        // BAUD
        Console.WriteLine("Baud: 300, 600, 1200, 2400, 4800, 9600, 19200, 3840, 57600, etc.");
        
        // DATABITS
        Console.WriteLine("DataBits: 8 or 7");

        Console.WriteLine("Available Parity options:");
        foreach (string s in Enum.GetNames(typeof(Parity)))
        {
            Console.WriteLine("   {0}", s);
        }
        Console.WriteLine("Available StopBits options:");
        foreach (string s in Enum.GetNames(typeof(StopBits)))
        {
            Console.WriteLine("   {0}", s);
        }
        Console.WriteLine("Available Handshake options:");
        foreach (string s in Enum.GetNames(typeof(Handshake)))
        {
            Console.WriteLine("   {0}", s);
        }
    }

    public static void Main(string[] args)
    {
        if (args.Length < 6)
        {
            showCommandArguments();
            Environment.Exit(-1);
        }

        string message = string.Empty;
        StringComparer stringComparer = StringComparer.OrdinalIgnoreCase;

        Thread readThread = new Thread(Read);

        // Create a new SerialPort object with default settings.
        _serialPort = new SerialPort();

        // Allow the user to set the appropriate properties.
        _serialPort.PortName = args[0];
        _serialPort.BaudRate = int.Parse(args[1]);
        _serialPort.DataBits = int.Parse(args[2].ToUpperInvariant()); ;
        _serialPort.Parity = (Parity)Enum.Parse(typeof(Parity), args[3], true);
        _serialPort.StopBits = (StopBits)Enum.Parse(typeof(StopBits), args[4], true);
        _serialPort.Handshake = (Handshake)Enum.Parse(typeof(Handshake), args[5], true);

        // Set the read/write timeouts
        _serialPort.ReadTimeout = 500;
        _serialPort.WriteTimeout = 500;

        try
        {
            _serialPort.Open();
        }
        catch (Exception e)
        {
            // Serial cable not connected, wrong COM port, or some such mishap...
            Console.WriteLine(e.Message);
            Environment.Exit(-2);
        }

        _continue = true;
        readThread.Start();  // Go ahead and enable this so we get echo back of any streamed input from file

        if (args.Length > 6)
        {
            System.IO.StreamReader myFile = null;
            try
            {
                myFile = new System.IO.StreamReader(args[6]);

                // Just go ahead and read the entire file into memory.  We'll assume this is a fairly small script file.
                // Note that this file will end up being stored twice into memory (in order to support REPEAT mode).
                message = myFile.ReadToEnd();

                myFile.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to open or buffer the specified input file.");
                Console.WriteLine(e.Message);
                Environment.Exit(-3);
            }

            Console.WriteLine("Sending file [{0}]", args[6]);

            string messageOriginal = message;
            int repeat_count = 1;

            // Go ahead and start sending the file content over serial port...
            do
            {
                while (true)
                {
                    // Look for next whole line in the input file...
                    int i = message.IndexOf('\n');
                    if (i == -1)
                    {
                        // Just send/process the remainder of the file content
                        sendMessageLine(message);
                        message = "";  // and clear it out as all being sent
                    }
                    else
                    {
                        // copy just the next whole line
                        string messageLine = message.Substring(0, i);
                        message = message.Substring(i + 1);  // remove that next line from the message buffer (keep just the remaining)

                        // Send/process the next whole line
                        sendMessageLine(messageLine);
                    }
                    if (message.Length < 1)
                    {
                        // We're done sending lines/content of the file.
                        break;
                    }
                }

                if (args.Length > 7)  // We're in repeat mode, so prepare do it again...
                {
                    ++repeat_count;
                    Console.WriteLine("  REPEATING {0} [{1}]  ", repeat_count, DateTime.Now.ToString());
                    message = messageOriginal;
                }
                else
                {
                    // Not in repeat mode, break out and proceed to "interactive mode"
                    break;
                }
            } while (true);

            Console.WriteLine("!!! END OF FILE");
        }

        Console.WriteLine("INTERACTIVE MODE: press ESC or CTRL+C to exit");

        ConsoleKeyInfo cki = new ConsoleKeyInfo();

        while (_continue && _serialPort.IsOpen)
        {
            while (Console.KeyAvailable == false)
            {
                Thread.Sleep(SLEEP_DELAY);  // Loop until input is entered.  Use small value -- not too small that we eat a lot of CPU checking for key, but not too slow that we're not ready when a key is finally pressed
            }

            cki = Console.ReadKey(true);

            if (cki.Key == ConsoleKey.Escape)
            {
                _continue = false;
            }
            else
            {
                _serialPort.Write("" + (char)cki.KeyChar);
                // the "natural" time of users pressing down/up on a key is enough delay, we don't need to add more delay.
                // Web sources give this average time as 70ms per keystroke.  In my own studies, a human can type about 6-10 characters in a second at peak speed and a good keyboard.
                // This 70ms number implies people typing about 14 characters in a second, which seem optimistic (but about half that is reasonable, which then matches
                // the 30-40ms time I use during the file sending delay to avoid filling the keyboard buffer)
            }
        }

        // Wait for the read thread to exit out...
        readThread.Join();

        // Gracefully clean up resources...
        _serialPort.Close();
    }

    public static void Read()
    {
        while (_continue)
        {
            try
            {
                // If any input back from the serial port, then show it...
                int ch = _serialPort.ReadChar();
                Console.Write((char)ch);
            }
            catch (TimeoutException) 
            {
                // no character received
            }
        }
    }

}