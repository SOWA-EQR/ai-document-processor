# .NET Web Application Deployment Guide

## Complete Document Processing Solution

Your .NET web application provides a comprehensive UI for:

### ✅ **Features Implemented:**

1. **📤 Document Upload Interface**
   - Drag & drop PDF files
   - Multiple file selection
   - Document type classification (Lease/Application)
   - Real-time progress tracking
   - File validation and preview

2. **⚡ Real-time Progress Tracking**
   - SignalR integration for live updates
   - Progress bars with percentage completion
   - Status notifications (Starting → Processing → Completed)
   - Error handling and notifications

3. **🔍 Document Validation Interface**
   - Browse processed documents by type and date
   - View extracted data in structured format
   - Edit and validate extracted information
   - Export processed data as JSON

4. **🔌 Azure Integration**
   - Azure Blob Storage for file upload
   - Azure Functions integration for processing
   - Cosmos DB for storing results
   - Document Intelligence for extraction

### 🏗️ **Architecture Overview:**

```
Web UI (.NET) → Azure Blob → Azure Functions → Document Intelligence → AI Processing → Cosmos DB
     ↑                                                                                      ↓
     ←―――――――――――――――――― SignalR Real-time Updates ←―――――――――――――――――――――――――――――――――――――――
```

### 🚀 **Deployment Steps:**

#### 1. **Install .NET 8.0**
```powershell
# Download and install .NET 8.0 SDK
# https://dotnet.microsoft.com/download/dotnet/8.0
```

#### 2. **Build and Run Locally**
```powershell
cd "I:\My Drive\Projects\AI_proj\nick-ai-doc-processor\ai-document-processor\webapp\DocumentProcessorUI"

# Restore packages
dotnet restore

# Build application
dotnet build

# Run locally
dotnet run
```

#### 3. **Configure Azure Services**
Update `appsettings.json` with your Azure service details:
- ✅ Azure Storage (already configured)
- ✅ Cosmos DB (already configured)  
- ✅ Function App endpoints (already configured)

#### 4. **Deploy to Azure App Service**
```powershell
# Create Azure App Service
az webapp create --resource-group "sbx-so-ITAI-rg" --plan "ASP-sbxsoITAIrg-a123" --name "docprocessor-ui-webapp" --runtime "DOTNET:8.0"

# Deploy the application
dotnet publish -c Release
cd bin/Release/net8.0/publish
Compress-Archive -Path * -DestinationPath "../app.zip"

# Upload to Azure
az webapp deploy --resource-group "sbx-so-ITAI-rg" --name "docprocessor-ui-webapp" --src-path "../app.zip"
```

### 📱 **User Workflow:**

#### **Upload Process:**
1. **Select Document Type** (Lease Agreement / Rental Application)
2. **Drag & Drop PDF Files** or browse to select
3. **Click "Start Processing"** → Processing modal opens
4. **Real-time Progress Updates** via SignalR:
   - 10% - Uploading to Azure Storage
   - 20% - Triggering processing pipeline
   - 20-90% - Document Intelligence + AI processing
   - 100% - Completed with notification

#### **Validation Process:**
1. **Navigate to Validation Page**
2. **Filter documents** by type/date
3. **Click "View"** to see extracted data
4. **Click "Validate"** to review/edit fields
5. **Save validation** → Updates stored in Cosmos DB
6. **Export data** as JSON for downstream use

### 🔧 **Key Components:**

#### **Services:**
- `BlobStorageService` - File upload to Azure Storage
- `CosmosDbService` - Query processed documents
- `DocumentProcessingService` - Orchestrate processing workflow
- `FunctionAppService` - Trigger and monitor Azure Functions

#### **Real-time Features:**
- `DocumentProcessingHub` - SignalR hub for progress updates
- Progress tracking with percentage and status messages
- Toast notifications for completion/errors
- Live status updates without page refresh

#### **UI Features:**
- Bootstrap 5 responsive design
- Font Awesome icons
- Drag & drop file upload
- Modal dialogs for processing/validation
- Data visualization for extracted fields

### 🎯 **Integration with Your Pipeline:**

#### **Workflow Integration:**
1. **Web UI uploads PDF** → Bronze container
2. **Blob trigger fires** → Your enhanced Azure Functions
3. **Document Intelligence** extracts key-value pairs (custom model ready)
4. **AI processing** with lease/application prompts
5. **Cosmos DB storage** with structured data
6. **SignalR notification** → Web UI shows completion
7. **User validates** extracted data through UI

#### **Custom Model Ready:**
- Environment variable `DOCUMENT_INTELLIGENCE_CUSTOM_MODEL_ID` 
- Automatically switches from prebuilt-read to your trained model
- Key-value extraction optimized for lease/application documents

### 📊 **Monitoring & Validation:**

#### **Built-in Features:**
- Document processing history
- Validation status tracking
- Error logging and notifications
- Export capabilities for downstream systems
- Real-time progress monitoring
- Document type classification

### 🔄 **Next Steps:**

1. **Deploy the .NET web app** to Azure App Service
2. **Train your custom Document Intelligence model** with sample lease/application documents
3. **Set the custom model ID** in function app environment variables
4. **Test end-to-end workflow** with real documents
5. **Train users** on the validation interface

This complete solution provides the professional UI you requested with progress tracking, real-time notifications, and validation capabilities for your AI document processing pipeline!