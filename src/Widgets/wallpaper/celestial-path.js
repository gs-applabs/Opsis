(() => {
 'use strict';
 const RAD = Math.PI/180, DEG = 180/Math.PI;
 function position(date, latitude, longitude, moon = false) {
   if (!(date instanceof Date) || !Number.isFinite(date.getTime()) || !Number.isFinite(latitude) || !Number.isFinite(longitude)) return null;
   const lat = Math.max(-90, Math.min(90, latitude))*RAD;
   const d = (date.getTime()-Date.UTC(2000,0,1,12))/86400000;
   const yearDay = (date.getTime()-Date.UTC(date.getUTCFullYear(),0,1))/86400000;
   const gamma = 2*Math.PI/365*(yearDay + (date.getUTCHours()-12)/24);
   const eq = 229.18*(.000075+.001868*Math.cos(gamma)-.032077*Math.sin(gamma)-.014615*Math.cos(2*gamma)-.040849*Math.sin(2*gamma));
   let decl = .006918-.399912*Math.cos(gamma)+.070257*Math.sin(gamma)-.006758*Math.cos(2*gamma)+.000907*Math.sin(2*gamma)-.002697*Math.cos(3*gamma)+.00148*Math.sin(3*gamma);
   let hour = (date.getUTCHours()*60+date.getUTCMinutes()+date.getUTCSeconds()/60+eq+4*longitude-720)/4*RAD;
   if (moon) {
      // Approximate lunar transit with a phase-driven offset; not an ephemeris.
      // This keeps the visual on a latitude/date-based path, unlike a fixed sine arc.
      const phase = ((d + 25.873)/29.530588853%1+1)%1;
      hour += (phase*2-1)*Math.PI;
      decl += 5.14*RAD*Math.sin(2*Math.PI*phase);
   }
   const altitude = Math.asin(Math.max(-1, Math.min(1, Math.sin(lat)*Math.sin(decl)+Math.cos(lat)*Math.cos(decl)*Math.cos(hour))));
   const az = (Math.atan2(Math.sin(hour), Math.cos(hour)*Math.sin(lat)-Math.tan(decl)*Math.cos(lat))*DEG+180+360)%360;
   return { altitude: altitude*DEG, azimuth: az, approximate: moon };
 }
 if (typeof module !== 'undefined') module.exports = { position };
 else window.OpsisCelestialPath = { position };
})();
