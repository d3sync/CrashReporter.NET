// Data contracts of the Doctor Dump CrashReporterReportUploader SOAP service.
// Originally generated from the service WSDL by the Visual Studio "Web Reference" tool.
#pragma warning disable 1591

namespace CrashReporterDotNET.com.drdump {
    using System;
    using System.Xml.Serialization;
    
    /// <remarks/>
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="https://www.drdump.com/services")]
    public partial class ClientLib {
        
        private ushort v1Field;
        
        private ushort v2Field;
        
        private ushort v3Field;
        
        private ushort v4Field;
        
        /// <remarks/>
        public ushort V1 {
            get {
                return this.v1Field;
            }
            set {
                this.v1Field = value;
            }
        }
        
        /// <remarks/>
        public ushort V2 {
            get {
                return this.v2Field;
            }
            set {
                this.v2Field = value;
            }
        }
        
        /// <remarks/>
        public ushort V3 {
            get {
                return this.v3Field;
            }
            set {
                this.v3Field = value;
            }
        }
        
        /// <remarks/>
        public ushort V4 {
            get {
                return this.v4Field;
            }
            set {
                this.v4Field = value;
            }
        }
    }
    
    /// <remarks/>
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="https://www.drdump.com/services")]
    public partial class DetailedExceptionDescription {
        
        private string developerMessageField;
        
        private ExceptionDescription exceptionField;
        
        private byte[] pngScreenShotField;
        
        private string userDescriptionField;
        
        private string userEmailField;
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string DeveloperMessage {
            get {
                return this.developerMessageField;
            }
            set {
                this.developerMessageField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public ExceptionDescription Exception {
            get {
                return this.exceptionField;
            }
            set {
                this.exceptionField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(DataType="base64Binary", IsNullable=true)]
        public byte[] PngScreenShot {
            get {
                return this.pngScreenShotField;
            }
            set {
                this.pngScreenShotField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string UserDescription {
            get {
                return this.userDescriptionField;
            }
            set {
                this.userDescriptionField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string UserEmail {
            get {
                return this.userEmailField;
            }
            set {
                this.userEmailField = value;
            }
        }
    }
    
    /// <remarks/>
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="https://www.drdump.com/services")]
    public partial class ExceptionDescription {
        
        private string clrVersionField;
        
        private System.DateTime crashDateField;
        
        private ExceptionInfo exceptionField;
        
        private string exceptionStringField;
        
        private string osField;
        
        private int pCIDField;
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string ClrVersion {
            get {
                return this.clrVersionField;
            }
            set {
                this.clrVersionField = value;
            }
        }
        
        /// <remarks/>
        public System.DateTime CrashDate {
            get {
                return this.crashDateField;
            }
            set {
                this.crashDateField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public ExceptionInfo Exception {
            get {
                return this.exceptionField;
            }
            set {
                this.exceptionField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string ExceptionString {
            get {
                return this.exceptionStringField;
            }
            set {
                this.exceptionStringField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string OS {
            get {
                return this.osField;
            }
            set {
                this.osField = value;
            }
        }
        
        /// <remarks/>
        public int PCID {
            get {
                return this.pCIDField;
            }
            set {
                this.pCIDField = value;
            }
        }
    }
    
    /// <remarks/>
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="https://www.drdump.com/services")]
    public partial class ExceptionInfo {
        
        private int hResultField;
        
        private ExceptionInfo innerExceptionField;
        
        private string messageField;
        
        private string sourceField;
        
        private string stackTraceField;
        
        private string typeField;
        
        /// <remarks/>
        public int HResult {
            get {
                return this.hResultField;
            }
            set {
                this.hResultField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public ExceptionInfo InnerException {
            get {
                return this.innerExceptionField;
            }
            set {
                this.innerExceptionField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string Message {
            get {
                return this.messageField;
            }
            set {
                this.messageField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string Source {
            get {
                return this.sourceField;
            }
            set {
                this.sourceField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string StackTrace {
            get {
                return this.stackTraceField;
            }
            set {
                this.stackTraceField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string Type {
            get {
                return this.typeField;
            }
            set {
                this.typeField = value;
            }
        }
    }
    
    /// <remarks/>
    [System.Xml.Serialization.XmlIncludeAttribute(typeof(StopResponse))]
    [System.Xml.Serialization.XmlIncludeAttribute(typeof(NeedReportResponse))]
    [System.Xml.Serialization.XmlIncludeAttribute(typeof(ErrorResponse))]
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="https://www.drdump.com/services")]
    public partial class Response {
        
        private string clientIDField;
        
        private byte[] contextField;
        
        private int dumpGroupIDField;
        
        private bool dumpGroupIDFieldSpecified;
        
        private int dumpIDField;
        
        private bool dumpIDFieldSpecified;
        
        private byte[] garbageField;
        
        private int problemIDField;
        
        private bool problemIDFieldSpecified;
        
        private string urlToProblemField;
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string ClientID {
            get {
                return this.clientIDField;
            }
            set {
                this.clientIDField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(DataType="base64Binary", IsNullable=true)]
        public byte[] Context {
            get {
                return this.contextField;
            }
            set {
                this.contextField = value;
            }
        }
        
        /// <remarks/>
        public int DumpGroupID {
            get {
                return this.dumpGroupIDField;
            }
            set {
                this.dumpGroupIDField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool DumpGroupIDSpecified {
            get {
                return this.dumpGroupIDFieldSpecified;
            }
            set {
                this.dumpGroupIDFieldSpecified = value;
            }
        }
        
        /// <remarks/>
        public int DumpID {
            get {
                return this.dumpIDField;
            }
            set {
                this.dumpIDField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool DumpIDSpecified {
            get {
                return this.dumpIDFieldSpecified;
            }
            set {
                this.dumpIDFieldSpecified = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(DataType="base64Binary", IsNullable=true)]
        public byte[] Garbage {
            get {
                return this.garbageField;
            }
            set {
                this.garbageField = value;
            }
        }
        
        /// <remarks/>
        public int ProblemID {
            get {
                return this.problemIDField;
            }
            set {
                this.problemIDField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlIgnoreAttribute()]
        public bool ProblemIDSpecified {
            get {
                return this.problemIDFieldSpecified;
            }
            set {
                this.problemIDFieldSpecified = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string UrlToProblem {
            get {
                return this.urlToProblemField;
            }
            set {
                this.urlToProblemField = value;
            }
        }
    }
    
    /// <remarks/>
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="https://www.drdump.com/services")]
    public partial class StopResponse : Response {
    }
    
    /// <remarks/>
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="https://www.drdump.com/services")]
    public partial class NeedReportResponse : Response {
    }
    
    /// <remarks/>
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="https://www.drdump.com/services")]
    public partial class ErrorResponse : Response {
        
        private string errorField;
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string Error {
            get {
                return this.errorField;
            }
            set {
                this.errorField = value;
            }
        }
    }
    
    /// <remarks/>
    [System.SerializableAttribute()]
    [System.Xml.Serialization.XmlTypeAttribute(Namespace="https://www.drdump.com/services")]
    public partial class Application {
        
        private string appNameField;
        
        private string applicationGUIDField;
        
        private string companyNameField;
        
        private string emailField;
        
        private string mainModuleField;
        
        private ushort v1Field;
        
        private ushort v2Field;
        
        private ushort v3Field;
        
        private ushort v4Field;
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string AppName {
            get {
                return this.appNameField;
            }
            set {
                this.appNameField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string ApplicationGUID {
            get {
                return this.applicationGUIDField;
            }
            set {
                this.applicationGUIDField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string CompanyName {
            get {
                return this.companyNameField;
            }
            set {
                this.companyNameField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string Email {
            get {
                return this.emailField;
            }
            set {
                this.emailField = value;
            }
        }
        
        /// <remarks/>
        [System.Xml.Serialization.XmlElementAttribute(IsNullable=true)]
        public string MainModule {
            get {
                return this.mainModuleField;
            }
            set {
                this.mainModuleField = value;
            }
        }
        
        /// <remarks/>
        public ushort V1 {
            get {
                return this.v1Field;
            }
            set {
                this.v1Field = value;
            }
        }
        
        /// <remarks/>
        public ushort V2 {
            get {
                return this.v2Field;
            }
            set {
                this.v2Field = value;
            }
        }
        
        /// <remarks/>
        public ushort V3 {
            get {
                return this.v3Field;
            }
            set {
                this.v3Field = value;
            }
        }
        
        /// <remarks/>
        public ushort V4 {
            get {
                return this.v4Field;
            }
            set {
                this.v4Field = value;
            }
        }
    }
}
