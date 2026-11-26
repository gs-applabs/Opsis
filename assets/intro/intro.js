'use strict';
const strings={
 greek:['Ένα οικοσύστημα, αμέτρητες δυνατότητες','Παράλειψη','Desktop Customization'],
 english:['One ecosystem, endless possibilities','Skip','Desktop Customization'],
 german:['Ein Ökosystem, unendliche Möglichkeiten','Überspringen','Desktop-Anpassung'],
 french:['Un écosystème, des possibilités infinies','Passer','Personnalisation du bureau'],
 spanish:['Un ecosistema, infinitas posibilidades','Omitir','Personalización del escritorio'],
 korean:['하나의 생태계, 무한한 가능성','건너뛰기','데스크톱 사용자 지정'],
 chinese:['一个生态系统，无限可能','跳过','桌面个性化'],
 italian:['Un ecosistema, infinite possibilità','Salta','Personalizzazione del desktop'],
 russian:['Одна экосистема, безграничные возможности','Пропустить','Настройка рабочего стола'],
 japanese:['ひとつのエコシステム、無限の可能性','スキップ','デスクトップのカスタマイズ'],
 portuguese:['Um ecossistema, infinitas possibilidades','Ignorar','Personalização do ambiente de trabalho'],
 turkish:['Tek bir ekosistem, sonsuz olanaklar','Atla','Masaüstü özelleştirme'],
 arabic:['منظومة واحدة، إمكانيات لا حصر لها','تخطٍّ','تخصيص سطح المكتب'],
 hindi:['एक इकोसिस्टम, अनंत संभावनाएँ','छोड़ें','डेस्कटॉप अनुकूलन']
};
const requested=new URLSearchParams(location.search).get('lang');const lang=Object.hasOwn(strings,requested)?requested:'english';
const [slogan,skip,subtitle]=strings[lang];const video=document.getElementById('intro'),plate=document.getElementById('plate'),caption=document.getElementById('caption'),button=document.getElementById('skip');
const sound=document.getElementById('sound');
const soundLabels={greek:['Σίγαση','Ενεργοποίηση ήχου'],english:['Mute','Unmute'],german:['Stummschalten','Ton einschalten'],french:['Couper le son','Activer le son'],spanish:['Silenciar','Activar sonido'],korean:['음소거','음소거 해제'],chinese:['静音','取消静音'],italian:['Disattiva audio','Attiva audio'],russian:['Выключить звук','Включить звук'],japanese:['ミュート','ミュート解除'],portuguese:['Silenciar','Ativar som'],turkish:['Sesi kapat','Sesi aç'],arabic:['كتم الصوت','تشغيل الصوت'],hindi:['ध्वनि बंद करें','ध्वनि चालू करें']};
function updateSound(){const label=soundLabels[lang][video.muted?1:0];sound.textContent=video.muted?'🔇':'🔊';sound.title=label;sound.setAttribute('aria-label',label);sound.setAttribute('aria-pressed',String(video.muted));}
video.volume=.72;updateSound();sound.addEventListener('click',()=>{video.muted=!video.muted;updateSound();});
button.textContent=skip+' ⏵';caption.dir=lang==='arabic'?'rtl':'ltr';
const smooth=(a,b,t)=>{const u=Math.max(0,Math.min(1,(t-a)/(b-a)));return u*u*(3-2*u);};
let frame=0,finished=false;
function done(reason){if(finished)return;finished=true;cancelAnimationFrame(frame);video.pause();window.chrome?.webview?.postMessage(reason);}
function tick(){if(finished)return;const t=video.currentTime;
 // Greek master remains exactly as approved. Only non-Greek installs cover the baked caption strip.
 plate.hidden=lang==='greek'||t<10.25;
 if(!plate.hidden){if(t<12.5){caption.textContent=subtitle;caption.style.opacity=String(smooth(10.3,11,t)*(1-smooth(11.9,12.85,t)));}else{caption.textContent=slogan;caption.style.opacity=String(smooth(12.55,13.55,t));}}
 frame=requestAnimationFrame(tick);
}
button.addEventListener('click',()=>done('skip'));document.addEventListener('keydown',e=>{if(e.key==='Escape')done('skip');});video.addEventListener('ended',()=>done('ended'));video.addEventListener('error',()=>done('error'));
if(matchMedia('(prefers-reduced-motion: reduce)').matches){video.muted=true;sound.hidden=true;video.pause();document.getElementById('stage').classList.add('still');plate.hidden=false;caption.textContent=slogan;caption.style.opacity='1';setTimeout(()=>done('ended'),3000);}else{frame=requestAnimationFrame(tick);video.play().catch(()=>{if(finished)return;video.muted=true;updateSound();video.play().catch(()=>done('error'));});}
