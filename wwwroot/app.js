const el = id => document.getElementById(id);
async function api(path, method="GET", data) {
    const response = await fetch("/api/"+path, {method, headers:{"Content-Type":"application/json","X-GameHub":"1"}, body:data===undefined?undefined:JSON.stringify(data)});
    if(!response.ok) throw Error("Request failed ("+response.status+")");
    return response.status===204?null:response.json();
}
function message(text){el("message").textContent=text}
async function action(task){try{await task()}catch(e){message(e.message)}}
async function show(library=false){
    const games=await api(library?"library":"games?q="+encodeURIComponent(el("search").value));
    el("games").replaceChildren();
    games.forEach(game=>{
        const card=document.createElement("article");
        for(const [tag,text] of [["small",game.genre],["h2",game.name],["p",game.description],["p",game.priceCents?"€"+(game.priceCents/100).toFixed(2):"Free"]]){
            const node=document.createElement(tag);node.textContent=text;card.append(node);
        }
        if(!library){const button=document.createElement("button");button.textContent="Add to demo library";button.onclick=()=>action(async()=>{await api("library/"+game.id,"POST");message("Added to library")});card.append(button)}
        el("games").append(card);
    });
}
for(const mode of ["login","register"]) el(mode).onclick=()=>action(async()=>{await api(mode,"POST",{email:el("email").value,password:el("password").value});el("password").value="";message(mode==="login"?"Signed in":"Account created. Sign in to continue.");});
el("logout").onclick=()=>action(async()=>{await api("logout","POST");message("Signed out");await show()});
el("catalogue").onclick=()=>action(()=>show());
el("library").onclick=()=>action(()=>show(true));
el("search").onchange=()=>action(()=>show());
action(()=>show());
