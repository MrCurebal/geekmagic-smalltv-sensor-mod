#include <Arduino.h>
#include <EEPROM.h>
#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>
#include <ESP8266HTTPUpdateServer.h>
#include <ArduinoJson.h>
#include <Adafruit_GFX.h>
#include <Adafruit_ST7789.h>
#include <Fonts/FreeSansBold24pt7b.h>
#include <Fonts/FreeSansBold18pt7b.h>
#include "private_config.h"
#include <math.h>

// SmallTV Ultra: ST7789, SPI mode 3, active-low backlight on GPIO5.
Adafruit_ST7789 tft(15, 0, 2);
GFXcanvas16 tile(220,40);
ESP8266WebServer server(80);
ESP8266HTTPUpdateServer updater;
struct Metrics { float casing=NAN, ct=NAN, cl=NAN, gt=NAN, gl=NAN, ram=NAN, used=NAN, total=NAN; } m;
uint32_t lastData=0, lastJoin=0, lastDraw=0, received=0;
bool dirty=true, wasStale=true, fullDisplay=true;
String pendingSsid, pendingPass;
uint8_t brightness=32, rotation=2;
bool displayInverted=true;
uint8_t panelCount=4, splitDirection=0, panelMetrics[4]={0,1,2,3};
const char* metricIds[]={"case_temp","cpu_temp","ram_load","gpu_temp","cpu_load","gpu_load","ram_used","ram_total"};
const char* metricLabels[]={"CASE","CPU TEMP","RAM","GPU TEMP","CPU LOAD","GPU LOAD","RAM USED","RAM TOTAL"};
bool apiAuth(){if(server.header("X-Api-Key")==API_KEY)return true;if(server.authenticate("admin",API_KEY))return true;server.send(401,"application/json","{\"error\":\"Unauthorized\"}");return false;}
void saveSettings(){
  uint8_t settings[12]={0x47,0x4D,2,brightness,rotation,(uint8_t)(displayInverted?1:0),panelCount,splitDirection,panelMetrics[0],panelMetrics[1],panelMetrics[2],panelMetrics[3]};
  bool changed=false;for(int i=0;i<12;i++)if(EEPROM.read(i)!=settings[i])changed=true;
  if(changed){for(int i=0;i<12;i++)EEPROM.write(i,settings[i]);EEPROM.commit();}
}
void sendLayout(){JsonDocument d;d["count"]=panelCount;d["orientation"]=splitDirection?"stacked":"side-by-side";JsonArray a=d["metrics"].to<JsonArray>();for(int i=0;i<panelCount;i++)a.add(metricIds[panelMetrics[i]]);String out;serializeJson(d,out);server.send(200,"application/json",out);}
struct PanelRect{int x,y,w,h;};
PanelRect panelRect(int i){
  if(panelCount==1)return {0,0,240,240};
  if(panelCount==2)return splitDirection?PanelRect{0,i*120,240,120}:PanelRect{i*120,0,120,240};
  if(panelCount==3 && i==2)return {0,120,240,120};
  return {(i%2)*120,(i/2)*120,120,120};
}
float metricValue(uint8_t id){switch(id){case 0:return m.casing;case 1:return m.ct;case 2:return m.ram;case 3:return m.gt;case 4:return m.cl;case 5:return m.gl;case 6:return m.used;case 7:return m.total;default:return NAN;}}
float metricBar(uint8_t id){switch(id){case 1:case 4:return m.cl;case 3:case 5:return m.gl;case 2:case 6:return m.ram;case 7:return isnan(m.total)?NAN:100;default:return m.casing;}}
void pushTile(int x,int y,int w,int h){tft.startWrite();tft.setAddrWindow(x,y,w,h);for(int row=0;row<h;row++)tft.writePixels(tile.getBuffer()+row*220,w);tft.endWrite();}

const uint16_t BG=0x0862, WHITE=0xEF9E, GREY=0x8494, CYAN=0x2EBE, PURPLE=0xBC7F, TRACK=0x1947;

bool auth() { if(server.authenticate("admin", API_KEY)) return true; server.requestAuthentication(); return false; }
bool dataAuth() { return server.header("X-Api-Key")==API_KEY; }
void text(const String& s,int x,int y,uint16_t c,int size=1) { tft.setFont(nullptr);tft.setTextSize(size);tft.setTextColor(c,BG);tft.setCursor(x,y);tft.print(s); }
String val(float n) { return isnan(n)?"--":String((int)roundf(n)); }
void draw() {
  bool stale=lastData==0 || (uint32_t)(millis()-lastData)>15000;
  static String previous[8];static int widths[4]={0,0,0,0};
  const uint16_t colors[8]={0x6F35,CYAN,PURPLE,0xFD89,CYAN,0xFD89,PURPLE,PURPLE};
  if(fullDisplay){
    tft.fillScreen(BG);
    if(panelCount>=3){tft.drawFastHLine(7,119,226,TRACK);tft.drawFastVLine(119,7,panelCount==3?106:226,TRACK);}
    else if(panelCount==2){if(splitDirection)tft.drawFastHLine(7,119,226,TRACK);else tft.drawFastVLine(119,7,226,TRACK);}
    for(int i=0;i<panelCount;i++){
      PanelRect r=panelRect(i);uint8_t id=panelMetrics[i];
      text(metricLabels[id],r.x+12,r.y+11,colors[id],r.w<160 && strlen(metricLabels[id])>7?1:2);
      previous[i]="!";previous[i+4]="!";widths[i]=0;tft.fillRect(r.x+12,r.y+r.h-18,r.w-24,7,TRACK);
    }
  }
  for(int i=0;i<panelCount;i++){
    PanelRect r=panelRect(i);uint8_t id=panelMetrics[i];float value=metricValue(id), fill=metricBar(id);
    bool temp=id==0||id==1||id==3,gb=id>=6;
    String v=isnan(value)?"--":(gb?String(value,1):val(value));
    int fieldWidth=min(220,r.w-10),scale=r.w>=220 && r.h>=200?2:1;
    const GFXfont* numberFont=gb && fieldWidth<160?&FreeSansBold18pt7b:&FreeSansBold24pt7b;
    if(gb && v.length()>4)scale=1;
    int fieldHeight=scale==2?90:48;
    int fieldY=r.y+30+(r.h-120)/3;
    if(previous[i]!=v){
      previous[i]=v;
      tile.setFont(numberFont);tile.setTextSize(scale);int16_t bx,by;uint16_t bw,bh;tile.getTextBounds(v,0,0,&bx,&by,&bw,&bh);
      int unitSize=scale==2?3:2,unitWidth=gb?12*unitSize:(temp?11*unitSize:6*unitSize);
      if((int)bw+unitWidth+6>fieldWidth){scale=1;tile.setTextSize(1);tile.getTextBounds(v,0,0,&bx,&by,&bw,&bh);unitSize=2;unitWidth=gb?24:(temp?22:12);fieldHeight=48;}
      int tx=max(0,(fieldWidth-(int)bw-unitWidth-6)/2-(int)bx),baseline=scale==2?79:40,ux=tx+(int)bx+(int)bw+6;
      for(int stripe=0;stripe<fieldHeight;stripe+=40){
        tile.fillScreen(BG);tile.setFont(numberFont);tile.setTextSize(scale);tile.setTextColor(WHITE);tile.setCursor(tx,baseline-stripe);tile.print(v);
        tile.setFont(nullptr);tile.setTextSize(unitSize);tile.setTextColor(colors[id]);int unitY=baseline-unitSize*6-stripe;
        if(temp){tile.drawCircle(ux+2,unitY+3,2,colors[id]);tile.setCursor(ux+8,unitY);tile.print("C");}else{tile.setCursor(ux,unitY);tile.print(gb?"GB":"%");}
        pushTile(r.x+(r.w-fieldWidth)/2,fieldY+stripe,fieldWidth,min(40,fieldHeight-stripe));yield();
      }
    }
    String sub;
    if(stale)sub="WAITING FOR DATA";
    else if(isnan(value))sub="NO SENSOR";
    else if(id==0)sub="SYSTEM SENSOR";
    else if(id==2||gb)sub=(isnan(m.used)?"--":String(m.used,1))+" / "+val(m.total)+" GB";
    else sub="LOAD "+val(fill)+"%";
    if(previous[i+4]!=sub){previous[i+4]=sub;tile.fillScreen(BG);tile.setFont(nullptr);tile.setTextSize(1);tile.setTextColor(GREY);tile.setCursor(0,0);tile.print(sub);pushTile(r.x+10,r.y+r.h-37,r.w-20,10);}
    int w=isnan(fill)?0:(int)((r.w-24)*constrain(fill,0.0f,100.0f)/100),old=widths[i];
    if(w>old)tft.fillRect(r.x+12+old,r.y+r.h-18,w-old,7,colors[id]);else if(w<old)tft.fillRect(r.x+12+w,r.y+r.h-18,old-w,7,TRACK);widths[i]=w;
  }
  fullDisplay=false;dirty=false;wasStale=stale;lastDraw=millis();
}
float field(JsonDocument& d,const char* key,float low,float high){if(!d[key].is<float>()&&!d[key].is<int>())return NAN;float v=d[key].as<float>();return isfinite(v)&&v>=low&&v<=high?v:NAN;}
void setup() {
  EEPROM.begin(16);
  if(EEPROM.read(0)==0x47 && EEPROM.read(1)==0x4D && (EEPROM.read(2)==1||EEPROM.read(2)==2) && EEPROM.read(3)>=5 && EEPROM.read(3)<=100 && EEPROM.read(4)<=3 && EEPROM.read(5)<=1){brightness=EEPROM.read(3);rotation=EEPROM.read(4);displayInverted=EEPROM.read(5);}

  if(EEPROM.read(0)==0x47 && EEPROM.read(1)==0x4D && EEPROM.read(2)==2 && EEPROM.read(6)>=1 && EEPROM.read(6)<=4 && EEPROM.read(7)<=1){bool valid=true;for(int i=0;i<4;i++)if(EEPROM.read(8+i)>7)valid=false;if(valid){panelCount=EEPROM.read(6);splitDirection=EEPROM.read(7);for(int i=0;i<4;i++)panelMetrics[i]=EEPROM.read(8+i);}}
  pinMode(5,OUTPUT);analogWriteRange(255);analogWriteFreq(1000);analogWrite(5,255-(brightness*255/100));
  tft.init(240,240,SPI_MODE3);tft.setSPISpeed(40000000);tft.setRotation(rotation);tft.invertDisplay(displayInverted);tft.setTextWrap(false);
  text("STARTING...",20,100,WHITE); 
  WiFi.persistent(false);WiFi.mode(WIFI_STA);WiFi.hostname("pc-info-screen");WiFi.setAutoReconnect(true);WiFi.setPhyMode(WIFI_PHY_MODE_11N);WiFi.setOutputPower(20.5);WiFi.scanNetworks();WiFi.scanDelete();
  if(WiFi.SSID().length() && WiFi.SSID()!=LOADER_SSID)WiFi.begin();else WiFi.begin(LOADER_SSID,LOADER_PASS);
  server.collectHeaders("X-Api-Key");
  server.on("/health",HTTP_GET,[](){JsonDocument d;d["firmware"]="pc-info-native-1.5-en";d["ip"]=WiFi.localIP().toString();d["uptime"]=millis()/1000;d["heap"]=ESP.getFreeHeap();d["frames"]=received;d["ageSeconds"]=lastData?(millis()-lastData)/1000:0;d["stale"]=!lastData||(millis()-lastData)>15000;d["storageWritesForMetrics"]=0;d["rotation"]=rotation;d["brightness"]=brightness;d["layout"]=panelCount==4?"grid-2x2":"custom";d["panels"]=panelCount;d["language"]="en";d["units"]="large-bottom-right";d["renderer"]="partial-buffered";d["sketchMd5"]=ESP.getSketchMD5();String out;serializeJson(d,out);server.send(200,"application/json",out);});
  server.on("/api/metrics",HTTP_GET,[](){if(!dataAuth()&&!auth())return;JsonDocument d;d["CaseTemp"]=m.casing;d["CpuTemp"]=m.ct;d["CpuLoad"]=m.cl;d["GpuTemp"]=m.gt;d["GpuLoad"]=m.gl;d["RamLoad"]=m.ram;d["RamUsed"]=m.used;d["RamTotal"]=m.total;String out;serializeJson(d,out);server.send(200,"application/json",out);});
  server.on("/api/metrics",HTTP_POST,[](){if(!dataAuth()){server.send(401,"text/plain","Unauthorized");return;}if(server.arg("plain").length()>2048){server.send(413,"text/plain","Too large");return;}JsonDocument d;if(deserializeJson(d,server.arg("plain"))){server.send(400,"text/plain","Invalid JSON");return;}
    m.casing=field(d,"CaseTemp",1,125);m.ct=field(d,"CpuTemp",1,125);m.cl=field(d,"CpuLoad",0,100);m.gt=field(d,"GpuTemp",1,125);m.gl=field(d,"GpuLoad",0,100);m.ram=field(d,"RamLoad",0,100);m.used=field(d,"RamUsed",0,1024);m.total=field(d,"RamTotal",0,1024);
    lastData=millis();received++;dirty=true;server.send(200,"application/json","{\"ok\":true,\"storage\":\"ram\"}");});
  server.on("/",HTTP_GET,[](){if(!auth())return;server.send(200,"text/html; charset=utf-8",F("<!doctype html><meta name='viewport' content='width=device-width'><title>PC Monitor</title><body style='background:#0b1220;color:#eee;font:18px system-ui;max-width:600px;margin:40px auto;padding:20px'><h1>PC Monitor</h1><p>Receives CPU, GPU, RAM and system sensor data from your computer. Gauges are drawn on the device; live updates stay in RAM.</p><form action='/display' method='post'><label>Brightness <input type='range' name='brightness' min='5' max='100' value='32'></label><p><label>Rotation <select name='rotation'><option value='0'>0</option><option value='1'>90</option><option value='2' selected>180</option><option value='3'>270</option></select></label></p><label>Colors <select name='inversion'><option value='1'>Normal IPS</option><option value='0'>Invert</option></select></label><p><button>Apply</button></p></form><p><a href='/network'>Wi-Fi settings</a> | <a href='/health'>Status</a> | <a href='/update'>Update firmware</a></p><p>Display settings are saved on the device.</p>"));});
  server.on("/display",HTTP_POST,[](){if(!auth())return;if(server.hasArg("brightness")){brightness=constrain(server.arg("brightness").toInt(),5,100);analogWrite(5,255-(brightness*255/100));}if(server.hasArg("rotation")){rotation=constrain(server.arg("rotation").toInt(),0,3);tft.setRotation(rotation);}if(server.hasArg("inversion")){displayInverted=server.arg("inversion")=="1";tft.invertDisplay(displayInverted);}
    saveSettings();
    dirty=true;fullDisplay=true;server.sendHeader("Location","/");server.send(303);});
  server.on("/network",HTTP_GET,[](){if(!auth())return;server.send(200,"text/html; charset=utf-8",F("<!doctype html><meta name='viewport' content='width=device-width'><title>Wi-Fi</title><h1>Wi-Fi settings</h1><form method='post'><p><label>Network name <input name='ssid' maxlength='32' required></label></p><p><label>Password <input type='password' name='password' minlength='8' maxlength='63' required></label></p><button>Connect and save</button></form><p>Only 2.4 GHz networks are supported.</p><a href='/'>Back</a>"));});
  server.on("/network",HTTP_POST,[](){if(!auth())return;String s=server.arg("ssid"),p=server.arg("password");if(!s.length()||s.length()>32||p.length()<8||p.length()>63){server.send(400,"text/plain","Invalid network settings");return;}pendingSsid=s;pendingPass=p;server.send(200,"text/html; charset=utf-8","<h1>Connecting</h1><p>Find the new address in your router client list.</p>");});
  server.on("/api/layout",HTTP_GET,[](){if(!apiAuth())return;sendLayout();});
  server.on("/api/layout",HTTP_POST,[](){
    if(!apiAuth())return;
    if(server.arg("plain").length()>512){server.send(413,"application/json","{\"error\":\"Too large\"}");return;}
    JsonDocument d;if(deserializeJson(d,server.arg("plain"))||!d["count"].is<int>()||!d["orientation"].is<const char*>()||!d["metrics"].is<JsonArray>()){server.send(400,"application/json","{\"error\":\"Invalid layout\"}");return;}
    int count=d["count"].as<int>();String orientation=d["orientation"].as<String>();JsonArray a=d["metrics"].as<JsonArray>();
    if(count<1||count>4||(orientation!="stacked"&&orientation!="side-by-side")||a.size()!=(size_t)count){server.send(400,"application/json","{\"error\":\"Invalid panel count or orientation\"}");return;}
    uint8_t selected[4]={panelMetrics[0],panelMetrics[1],panelMetrics[2],panelMetrics[3]};
    for(int i=0;i<count;i++){int match=-1;for(int j=0;j<8;j++)if(a[i].is<const char*>()&&a[i].as<String>()==metricIds[j])match=j;if(match<0){server.send(400,"application/json","{\"error\":\"Unknown metric\"}");return;}selected[i]=match;}
    bool changed=panelCount!=count||splitDirection!=(orientation=="stacked");for(int i=0;i<count;i++)if(panelMetrics[i]!=selected[i])changed=true;
    panelCount=count;splitDirection=orientation=="stacked";for(int i=0;i<4;i++)panelMetrics[i]=selected[i];
    if(changed){saveSettings();dirty=true;fullDisplay=true;}sendLayout();
  });
  updater.setup(&server,"/update","admin",API_KEY);
  server.begin();draw();
}
void loop() {
  server.handleClient();
  if(pendingSsid.length()){delay(100);WiFi.persistent(true);WiFi.begin(pendingSsid.c_str(),pendingPass.c_str());WiFi.persistent(false);pendingSsid="";pendingPass="";}
  if(WiFi.status()!=WL_CONNECTED && millis()-lastJoin>30000){lastJoin=millis();WiFi.reconnect();}
  // Add a protected recovery AP if station mode has not connected after 45 s.
  if(WiFi.status()!=WL_CONNECTED && millis()>45000 && WiFi.getMode()!=WIFI_AP_STA){WiFi.mode(WIFI_AP_STA);WiFi.softAP("PC-Info-Recovery",API_KEY);}
  bool stale=lastData==0 || millis()-lastData>15000;
  if((dirty && millis()-lastDraw>100)||stale!=wasStale)draw();
  yield();
}



