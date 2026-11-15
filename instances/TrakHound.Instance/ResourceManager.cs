using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound
{
    public static class ResourceManager
    {
        public static void AddTask(Task task)
        {
            //Console.WriteLine($"Task Added : {task.Id}");
        }

        public static void AddTask(int? taskId, string name = null)
        {
            //var thread = Thread.CurrentThread;
            //var threadId = thread.ManagedThreadId;

            //Console.WriteLine($"Task Added : {taskId} : {threadId} : {Thread.CurrentThread.Name} : {name}");
        }

        public static void RemoveTask(Task task) 
        {
            //Console.WriteLine($"Task Removed : {task.Id}");
        }

        public static void RemoveTask(int? taskId)
        {
            //var thread = Thread.CurrentThread;
            //var threadId = Environment.CurrentManagedThreadId;
            ////var threadId = thread.ManagedThreadId;

            //Console.WriteLine($"Task Removed : {taskId} : {threadId} : {Thread.CurrentThread.Name}");

            //var process = Process.GetCurrentProcess(); // getting current running process of the app
            //foreach (ProcessThread processThread in process.Threads)
            //{
            //    var threadTime = processThread.TotalProcessorTime.TotalMicroseconds;
            //    var processTime = process.TotalProcessorTime.TotalMicroseconds;

            //    if (threadTime > 0)
            //    {
            //        var processorUsage = threadTime / processTime;

            //        Console.WriteLine($"Thread : {processThread.Id} : {processThread.ThreadState} : {processorUsage.ToString("P4")}");
            //        //Console.WriteLine($"Thread : {processThread.Id} : {processThread.ThreadState} : {processThread.TotalProcessorTime} : {processThread.UserProcessorTime} : {processThread.PrivilegedProcessorTime}");

            //        // use pt.Id / pt.TotalProcessorTime / pt.UserProcessorTime / pt.PrivilegedProcessorTime
            //    }
            //}
        }
    }
}
