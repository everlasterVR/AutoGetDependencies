#define ENV_DEVELOPMENT
using System;
using System.Text;

// Licensed under Creative Commons Attribution 4.0 International https://creativecommons.org/licenses/by/4.0/
// (c) 2024 everlaster
namespace everlaster
{
    sealed class LogBuilder
    {
        readonly string _prefix;
        readonly StringBuilder _sb = new StringBuilder();

        public LogBuilder(string moduleName)
        {
            _prefix = moduleName;
        }

        public void Error(string error)
        {
            Clear();
            _sb.Append(error);
            LogError();
        }

        public void Exception(Exception e)
        {
            _sb.Clear().AppendFormat(e.ToString());
            LogException();
        }

        public void Exception(string message, Exception e)
        {
            _sb.Clear().AppendFormat($"{message}: {e}");
            LogException();
        }

        public void Message(string format, params object[] args)
        {
            Clear();
            _sb.AppendFormat(format, args);
            LogMessage();
        }

        public void Message(string message)
        {
            Clear();
            _sb.Append(message);
            LogMessage();
        }

        public void Debug(string message)
        {
            #if ENV_DEVELOPMENT
            {
                Clear();
                _sb.Append(message);
                LogDebug();
            }
            #endif
        }

        void Clear() => _sb.Clear().AppendFormat("{0}: ", _prefix);

        void LogError()
        {
            SuperController.LogError(_sb.ToString());
        }

        void LogException()
        {
            _sb.AppendFormat("\n{0}", new System.Diagnostics.StackTrace());
            SuperController.LogError(_sb.ToString());
        }

        void LogMessage() => SuperController.LogMessage(_sb.ToString());

        void LogDebug()
        {
            _sb.Insert(0, "[D] ");
            UnityEngine.Debug.Log(_sb.ToString());
        }
    }
}