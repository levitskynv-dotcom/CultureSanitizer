using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CultureSanitizer
{
    public static class StackSentinel
    {
        // Порог тревоги: сколько байт свободного стека считаем опасно малым.
        // Калибруется по числу N из теста — пока стоит консервативно.
        private static readonly ulong MinFree = 256 * 1024;

        [DllImport("kernel32.dll")]
        private static extern void GetCurrentThreadStackLimits(out UIntPtr lowLimit, out UIntPtr highLimit);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static unsafe ulong FreeStack()
        {
            var probe = stackalloc byte[1];           // адрес ~ текущая вершина стека
            GetCurrentThreadStackLimits(out var low, out var _);
            return (ulong)(UIntPtr)probe - (ulong)low; // свободно = вершина минус нижняя граница
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Check(string where)
        {
            var free = FreeStack();
            if (free < MinFree)
                throw new StackSentinelException(
                    $"Stack low: {free / 1024} KB free at '{where}'. Recursion suspected.");
        }
    }

    public class StackSentinelException : Exception
    {
        public StackSentinelException(string message) : base(message) { }
    }
}