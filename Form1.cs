using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.DirectX;
using Microsoft.DirectX.Direct3D;
using System.IO;

/*
 * Версия 0.0.1. going set - stability // движущиеся наборы, стабилизация
 * Первая из версий после предварительной разработки
 * Цели версии:
 * 1. исправление багов предыдущих версий при их обнаружении (будут описываться ниже):
 * 
 * 2. доработка механизмов, не отработанных в предыдущих версиях:
    * а) перерисовка текстур
    * б) создание файла-описания объектов для загрузки вместо прописанных в коде длинных загрузчиков
    * б-2) создание подпрограммы обработки объектов для облегчения создания новых блоков
    * в) доработка обработчика блоков - вода создаётся чёрной
    * в-2) д.о.б. - необходим менее длительный механизм
    * в-3) д.о.б. - необходимо переработать систему запуска с целью ускорения обработки блоков (может, стоит исключить обработку пустых блоков?)
    * г) доработка системы просчёта перемещения - застреваний быть не должно
    * г-2) дспп - плавание в воде
    * г-3) (относится к "г)" ) дспп- уменьшение количества просчётов с целью ускорения общей работы 
    * д) доработка отображения мира - создание конусовидного видимого участка для уменьшения бесполезно отрисоввываемых блоков
    * е) доработка системы просчёта выпавших предметов - складывание близколежащих предметов в стопки
    * е-2) дспвп - выпавшие блоки должны отскакивать и от блоков выше них (сейчас если есть блок выше, то выпавший блок "выстреливает" вверх)
    * ё) доработка системы дробления блоков, исключение удаления полного блока, удаление нескольких маленьких блоков одновременно
    * ж) доработка системы установки блоков - ставятся маленькие блоки
    * +з) доработка и подключение системы отображения повреждений у предметов
    * и) обобщение отображения предметов инвентаря и пояса
    * к) разделение параметров tach 0 и tach 1 на освещение от солнца и освещение от искуственных источников
 * 
 * 3. создание новых механизмов:
     * а) ИИ, управляющий НИП-ами
     * б) система двигающихся блоков, не связанных с жестко закрепленной сеткой блоков, но расценивающей её как препятствия (опираться на доработанную систему просчёта перемещений игрока)
     * в) полноценный режим "бога" - полный набор бесконечного числа ресурсов
     * в-2) перемещение в любых плоскостях без препятствий (с возможностью отключения режима)
     * г) расширение мира по вертикали, создание различающихся по Y участков
     * д) создание биомов, проработка генерации
     * е) создание системы крафта
     * ё) погода - механизм, выбирающий погоду
     * ё-2) погода - выпадение осадков
 * 
 * 4. проработка всей системы с целью максимального увеличения производительности
 * 4-1. создание независимых систем просчёта передвижения и отображения мира с целью сохранения общей скорости просчёта мира
 */

//структура маленьких блоков вводится совместно со структурой больших блоков, это поможет избежать слишком больших сохранений и лишних рассчётов наверное...
namespace speed
{
    public partial class Form1 : Form
    {
        Device dev;
        PresentParameters param = new PresentParameters();
        Material mat;
        Image ima;
        Image itx;
        Vector3 cp = new Vector3(20, 0, 0), cp0, ct = new Vector3(0, 0, 0), cup = new Vector3(0, 0, 1), sun, cpold;
        float pi = (float)Math.PI;
        bool going = true;
        bool InventoryOpen = false;//флаг отвечающий за открытие инвентаря
        bool techno = false;

        bool crushing = false;
        bool seting = false;

        VertexBuffer vbplane;
        IndexBuffer ibplane;
        CustomVertex.PositionNormalTextured[] dp;
        int[] di = new int[108];
        
        Random rand = new Random();
        const byte swalk = 1;
        const byte walk = 5;
        const byte runing = 15;

        float stepfb = 0, steprl = 0, Run = 5/*,  sq = 2f, lq*/;
        
        float sune = 255;
        float shiftsune = -0.01f;
        float accelerate = 0;
        bool flight = false;
        //bool light = false;
        byte cps = 60, framenum, fps, fpstick, sfps;
        Texture toolrepair;

        Line LIN;
        Vector2[] vec = new Vector2[5];
        Vector2[] vec1 = new Vector2[5];
        Point[] handinvcoord = new Point[20];//координаты клеток инвентаря видимых при движении
        Point[] invcoord = new Point[105];//координаты всего инвентаря
        Color handinvcol;
        sbyte handRUse = 0;
        sbyte handLUse = 10;
        byte leftsize = 1;
        byte rightsize = 1;
        byte backpacksize = 40;
        byte rsize = 0;
        const byte coeffwidth = 40;
        bool LeftHand = false;
        
        short precipnum = 0;
        svec3[, ,] boxcoord = new svec3[64, 64, 64];
        bvec3[, ,] smallboxcoord = new bvec3[8, 8, 8];//описывает координаты маленького блока, относительно координат основного
        float[] accelhand = new float[6]; //используется для описания ускорения рук при ударах
        byte visible = 12;
        byte wheather = 0;

        List<svec3> waterlist= new List<svec3>();

        bvec3[] shifting = new bvec3[7];

        
        Thread t0;

        struct objectOfInventory
        {
            public string type; //тип объекта блок/инструмент/пища/итд

            public string name; //имя объекта. используется при загрузке текстур
            public Texture texinv;  // текстура в инвентаре
            public Texture texworld; //текстура в мире
            public Texture alttexworld; //2-я текстура в мире

            //   public bool stacked;    //параметр складываемости. можно ли ложить в стопку не требуется из-за наличия stacksize
            public byte stacksize;   //определяет, сколько предметов в стопке
            /*Значения для преобразования*/
            public short standartHill; //определяет стандартное значение очков ХП для блока
            //public List<byte> typetransform; //типы блоков, в которые трансформируется этот блок при различных воздействиях
            //public string taching; //определяет типы воздействий
            /****************************************************************/
            public byte p1;      //параметр предмета. для пояса - размер правого кармана, для куртки и рюкзака - общий размер
            public byte p2;      //параметр предмета. для пояса - размер левого кармана/ для остальной одежды - защита
        }
        byte[,] inventory = new byte[105, 2];
        byte[] translateinv = new byte[2];
        short selectinv = -1;
        objectOfInventory[] objectlist = new objectOfInventory[200];

        struct precipitation
        {
            public byte type;
            public svec3 coordinate;
            public bvec3 speed;
        }

        precipitation[] prec = new precipitation[1000];

        struct userparam
        {
            public byte lefthandsize;
            public byte righthandsize;
            public byte zaksize;
            public byte robesize;
            public short hill;
            public Vector3 headcoordinate;//???????????????????
            public byte[,] inventory;
            byte[] translateinv;
        }


        struct dropobj
        {
            public byte type;
            public byte col;
            public ushort time;
            public Vector3 position;
            public Vector3 acceleration;
        }

        byte droped = 0;

        dropobj[] drop = new dropobj[100];
        //byte[,] dropinv = new byte[100, 3];
        //Vector3[,] dropinvcoord = new Vector3[100, 2];


        struct strmesh
        {
            public byte type;    //тип большого блока. определяет добываемость и реакцию на воздействия а также текстуры при цельном отображении
            /******Структура маленьких блоков******/
            public bool sb; //флаг отображения маленьких блоков. если 1, отображаются маленькие блоки
            public bool bvis;    //видимость блока
            public short hill;    //количество ХП блока отвечает за разрушение блоков и их трансформацию

            public bool[] vis; //видимость сторон блока, если блок целый
            public bool[] alt;  // испольование альтернативной текстуры
            public byte[,] taches;

            public byte[,,] sbtype;   //типы маленьких блоков внутри большого отвечают за отображение при sb = 1 и за добываемый материал
            public bool[,,,] sbvis; //видимость маленьких блоков. обобщена
            public bool[,,,] sbalt;// испольование альтернативной текстуры у маленьких блоков
            public byte[,,,,] sbtach;
            
            public strmesh(bool sb)
            {
                this.sb = sb;
                this.type = 0;
                this.hill = 1;
                this.bvis = true;
                if (sb)
                {
                    this.vis = null;
                    this.alt = null;
                    this.taches = null;

                    this.sbtype = new byte[4, 4, 4];
                    this.sbvis = new bool[4, 4, 4, 6];
                    this.sbalt = new bool[4, 4, 4, 6];
                    this.sbtach = new byte[4, 4, 4, 6, 3];
                }
                else
                {
                    this.vis = new bool[6];
                    this.alt = new bool[6];
                    this.taches = new byte[3, 6];

                    this.sbtype = null;
                    this.sbvis = null;
                    this.sbalt = null;
                    this.sbtach = null;
                }
            }
        }

        struct bvec3
        {
            public sbyte X;
            public sbyte Y;
            public sbyte Z;
            public bvec3(sbyte X, sbyte Y, sbyte Z)
            {
                this.X = X;
                this.Y = Y;
                this.Z = Z;
            }
        }
        struct bvec2
        {
            public sbyte X;
            public sbyte Y;
            public bvec2(sbyte X, sbyte Y)
            {
                this.X = X;
                this.Y = Y;
            }
        }
        struct svec3
        {
            public short X;
            public short Y;
            public short Z;
            public svec3(short X, short Y, short Z)
            {
                this.X = X;
                this.Y = Y;
                this.Z = Z;
            }
        }

        struct MapList
        {
            public short X;
            public short Y;
            public byte height;
            public strmesh[, ,] Map;

            public MapList(short X, short Y)
            {
                this.X = X;
                this.Y = Y;
                this.height = 0;
                this.Map = new strmesh[64, 64, 64];
            }
        }

        MapList[,] m = new MapList[4, 4];


        private void загрузитьToolStripMenuItem_Click(object sender, EventArgs e)
        {
            gametimer.Enabled = false;
            timer2.Enabled = false;

            FileStream file2 = new FileStream(Application.StartupPath + "\\save\\info.txt", FileMode.Open);
            StreamReader read = new StreamReader(file2);

            int startX = int.Parse(read.ReadLine());
            int startY = int.Parse(read.ReadLine());

            cp.X = (float)Convert.ToDouble(read.ReadLine());
            cp.Y = (float)Convert.ToDouble(read.ReadLine());
            cp.Z = (float)Convert.ToDouble(read.ReadLine());

            ct.X = (float)Convert.ToDouble(read.ReadLine());
            ct.Y = (float)Convert.ToDouble(read.ReadLine());
            ct.Z = (float)Convert.ToDouble(read.ReadLine());

            handRUse = sbyte.Parse(read.ReadLine());
            handLUse = sbyte.Parse(read.ReadLine());

            read.Close();
            file2.Close();

            for (byte mapp = 0; mapp < 16; mapp++)
            {
                m[mapp / 4, mapp % 4] = loadchunk((sbyte)(mapp / 4), (sbyte)(mapp % 4), (short)(startX - 1 + mapp / 4), (short)(startY - 1 + mapp % 4));
            }

            for (byte mapp = 0; mapp < 16; mapp++)   ///ПРОСЧЁТ ВИДИМОСТИ УЧАСТКОВ
            {
                m[mapp / 4, mapp % 4] = checkvisiblechunk(m, mapp / 4, mapp % 4)  ;
            }

            uX = 1;
            uY = 1;

           
            startgame();
        }

        private void сохранитьToolStripMenuItem_Click(object sender, EventArgs e)
        {
            gametimer.Enabled = false;
            for (byte mapp = 0; mapp < 16; mapp++)
            {
                savechunk(m[mapp / 4, mapp % 4]);
            }

            string s = Application.StartupPath + "\\save\\info.txt"; //название файла 
            FileStream file0 = new FileStream(s, FileMode.Create);  //при сохранении файл всегда создаётся заново
            file0.Close();


            FileStream file1 = new FileStream(s, FileMode.Append);
            StreamWriter write = new StreamWriter(file1);

            write.WriteLine(m[uX, uY].X.ToString());
            write.WriteLine(m[uX, uY].Y.ToString());

            write.WriteLine(cp.X.ToString());
            write.WriteLine(cp.Y.ToString());
            write.WriteLine(cp.Z.ToString());

            write.WriteLine(ct.X.ToString());
            write.WriteLine(ct.Y.ToString());
            write.WriteLine(ct.Z.ToString());

            write.WriteLine(handRUse.ToString());
            write.WriteLine(handLUse.ToString());

            write.Close();
            file1.Close();

            gametimer.Enabled = true;
        }


        private void savechunk(MapList saveM)
        {
          /*  using (BinaryWriter writer = new BinaryWriter(File.Open(Application.StartupPath + "test.dat", FileMode.OpenOrCreate)))
            {
                writer.Write(textBox1.Text);
            }
            using (BinaryReader read = new BinaryReader(File.Open(Application.StartupPath + "test.dat", FileMode.Open)))
            {
                textBox2.Text = read.ReadString();
            } */

            string s = Application.StartupPath + "\\save\\map\\" + saveM.X + "X" + saveM.Y + ".dat"; //название файла сохранения складывается из реальных номеров х и у участка
            using (BinaryWriter writer = new BinaryWriter(File.Open(s, FileMode.Create)))
            {
                for (byte x = 0; x < 64; x++)
                {
                    for (byte y = 0; y < 64; y++)
                    {
                        for (byte z = 0; z < 64; z++)
                        {
                            writer.Write(saveM.Map[x, y, z].sb);//сохраняется дробность блока
                            writer.Write(saveM.Map[x, y, z].type);//сохраняется тип блока
                            writer.Write(saveM.Map[x, y, z].bvis);//сохраняется видимость блока
                            writer.Write(saveM.Map[x, y, z].hill);//сохраняются ХП блока
                            if (!saveM.Map[x, y, z].sb)
                            {
                                for (byte vis = 0; vis < 6; vis++)
                                {
                                    writer.Write(saveM.Map[x, y, z].vis[vis]); //видимость сторон блока, если блок целый
                                    writer.Write(saveM.Map[x, y, z].alt[vis]);  // испольование альтернативной текстуры
                                    writer.Write(saveM.Map[x, y, z].taches[0, vis]);
                                    writer.Write(saveM.Map[x, y, z].taches[1, vis]);
                                }
                            }
                            else
                            {
                                for (byte sx = 0; sx < 4; sx++)
                                {
                                    for (byte sy = 0; sy < 4; sy++)
                                    {
                                        for (byte sz = 0; sz < 4; sz++)
                                        {
                                            writer.Write(saveM.Map[x, y, z].sbtype[sx, sy, sz]);   //типы маленьких блоков внутри большого отвечают за отображение при sb = 1 и за добываемый материал
                                            for (byte vis = 0; vis < 6; vis++)
                                            {
                                                writer.Write(saveM.Map[x, y, z].sbvis[sx, sy, sz, vis]); //видимость маленьких блоков. обобщена
                                                writer.Write(saveM.Map[x, y, z].sbalt[sx, sy, sz, vis]);// испольование альтернативной текстуры у маленьких блоков
                                                writer.Write(saveM.Map[x, y, z].sbtach[sx, sy, sz, vis, 0]);
                                                writer.Write(saveM.Map[x, y, z].sbtach[sx, sy, sz, vis, 1]);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                writer.Write(saveM.height);
            }


/*            FileStream file1 = new FileStream(s, FileMode.Append);
            StreamWriter write = new StreamWriter(file1);
            for (byte x = 0; x < 64; x++)
            {
                for (byte y = 0; y < 64; y++)
                {
                    for (byte z = 0; z < 64; z++)
                    {
                        //  write.Write(sm[x, y, z].type.ToString());
                        write.Write(saveM.Map[x, y, z].type);//сохраняются типы блоков
                        write.Write(" ");
                    }
                    write.WriteLine();
                }
            }
            write.WriteLine(saveM.height);//сохраняется высота
            write.Close();
            file1.Close();*/
        }
        private MapList loadchunk(sbyte mX, sbyte mY, short nX, short nY)
        {
            MapList mm = new MapList(nX, nY);
            try
            {


             
               

            string s = Application.StartupPath + "\\save\\map\\" + nX.ToString() + "X" + nY.ToString() + ".dat"; //название файла сохранения складывается из реальных номеров х и у участка
            using (BinaryReader reader = new BinaryReader(File.Open(s, FileMode.Open)))
            {
                for (byte x = 0; x < 64; x++)
                {
                    for (byte y = 0; y < 64; y++)
                    {
                        for (byte z = 0; z < 64; z++)
                        {
                            mm.Map[x, y, z]=new strmesh(reader.ReadBoolean());//сохраняется дробность блока
                            mm.Map[x, y, z].type = reader.ReadByte();//сохраняется тип блока
                            mm.Map[x, y, z].bvis = reader.ReadBoolean();//сохраняется видимость блока
                            mm.Map[x, y, z].hill = reader.ReadSByte();//сохраняются ХП блока
                            if (!mm.Map[x, y, z].sb)
                            {
                                for (byte vis = 0; vis < 6; vis++)
                                {
                                    mm.Map[x, y, z].vis[vis] = reader.ReadBoolean(); //видимость сторон блока, если блок целый
                                    mm.Map[x, y, z].alt[vis] = reader.ReadBoolean();  // испольование альтернативной текстуры
                                    mm.Map[x, y, z].taches[0, vis] = reader.ReadByte();
                                    mm.Map[x, y, z].taches[1, vis] = reader.ReadByte();
                                }
                            }
                            else
                            {
                                for (byte sx = 0; sx < 4; sx++)
                                {
                                    for (byte sy = 0; sy < 4; sy++)
                                    {
                                        for (byte sz = 0; sz < 4; sz++)
                                        {
                                            mm.Map[x, y, z].sbtype[sx, sy, sz] = reader.ReadByte();   //типы маленьких блоков внутри большого отвечают за отображение при sb = 1 и за добываемый материал
                                            for (byte vis = 0; vis < 6; vis++)
                                            {
                                                mm.Map[x, y, z].sbvis[sx, sy, sz, vis] = reader.ReadBoolean(); //видимость маленьких блоков
                                                mm.Map[x, y, z].sbalt[sx, sy, sz, vis] = reader.ReadBoolean();// испольование альтернативной текстуры у маленьких блоков
                                                mm.Map[x, y, z].sbtach[sx, sy, sz, vis, 0] = reader.ReadByte();
                                                mm.Map[x, y, z].sbtach[sx, sy, sz, vis, 1] = reader.ReadByte();
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                mm.height = reader.ReadByte();
            }


              /*  FileStream file2 = new FileStream(Application.StartupPath + "\\save\\map\\" + nX.ToString() + "X" + nY.ToString() + ".txt", FileMode.Open);
                StreamReader read = new StreamReader(file2);


                for (byte x = 0; x < 64; x++)
                {
                    for (byte y = 0; y < 64; y++)
                    {

                        for (byte z = 0; z < 64; z++)
                        {
                            mm.Map[x, y, z].type = (byte)(read.Read() - 48);
                            read.Read();
                        }
                        read.ReadLine();

                    }
                }
                mm.height = byte.Parse(read.ReadLine());
                read.Close();
                file2.Close();*/


                return mm;
            }

            catch
            {
                // MessageBox.Show("chunk "+nX.ToString()+ " " + nY.ToString() + " not load, generate");
                mm = generatechunk(nX, nY, mX, mY, (short)(hi(mX, mY) + rand.Next(2)));
                return mm;
            }
        }

        private strmesh[, ,] tree()
        {
            strmesh[, ,] tree0 = new strmesh[5, 5, 6];
            for (byte sx=0;sx<5;sx++)
            {
                for (byte sy = 0; sy < 5; sy++)
                {
                    for (byte sz = 0; sz < 6; sz++)
                    {
                        tree0[sx, sy, sz] = new strmesh(true);
                        tree0[sx, sy, sz].type = 2;
                        tree0[sx, sy, sz].bvis = true;
                    }
                }
            }
            byte startx = (byte)rand.Next(8, 12);
            byte starty = (byte)rand.Next(8, 12);
            byte fst = (byte)rand.Next(4, 8);
            for (byte st = 0; st < fst; st++)
            {
                sbyte x = (sbyte)(startx+ rand.Next(-1, 1));
                sbyte y = (sbyte)(starty + rand.Next(-1, 1));
                sbyte z = -1;
                byte rando= (byte)rand.Next(2, 6);
                if (rando == 1) rando = 0;
                for (byte step = 0; step < 18; step++)
                {
                    byte rnd = (byte)rand.Next(2, 40);
                    if (rnd < 5 & z > 2)
                    {
                        x = (sbyte)(x + shifting[rnd].X);
                        if (x < 2) x = 2;
                        if (x > 17) x = 17;
                        y = (sbyte)(y + shifting[rnd].Y);
                        if (y < 2) y = 2;
                        if (y > 17) y = 17;
                        //z = (byte)(z + shifting[rnd].Z);
                    }
                    else if(rnd >= 5 & rnd<10 & z > 2)
                    {
                        x = (sbyte)(x + shifting[rando].X);
                        if (x < 2) x = 2;
                        if (x > 17) x = 17;
                        y = (sbyte)(y + shifting[rando].Y);
                        if (y < 2) y = 2;
                        if (y > 17) y = 17;
                        //z = (byte)(z + shifting[rnd].Z);
                    }
                    else
                        z++;
                    byte wx = (byte)(x % 4);
                    byte wy = (byte)(y % 4);
                    byte wz = (byte)(z % 4);

                    byte wX = (byte)(x / 4);
                    byte wY = (byte)(y / 4);
                    byte wZ = (byte)(z / 4);
                    tree0[wX, wY, wZ].sbtype[wx, wy, wz] = 2;
                    // tree0[wX, wY, wZ].sbvis[wx, wy, wz,0] = true;
                }

                byte lx = (byte)(x - rand.Next(0,3));
                byte lfx = (byte)(x + rand.Next(0,3));
                for (byte tx=lx;tx<=lfx; tx++)
                {
                    byte ly = (byte)(y - rand.Next(0,3));
                    byte lfy = (byte)(y + rand.Next(0,3));
                    for (byte ty = ly; ty <= lfy; ty++)
                    {
                        byte lz = (byte)(z - rand.Next(1,3));
                        byte lfz = (byte)(z + rand.Next(1,5));
                        for (byte tz = lz; tz <= lfz; tz++)
                        {
                            byte wx = (byte)(tx % 4);
                            byte wy = (byte)(ty % 4);
                            byte wz = (byte)(tz % 4);
                            byte wX = (byte)(tx / 4);
                            byte wY = (byte)(ty / 4);
                            byte wZ = (byte)(tz / 4);
                            if (tree0[wX, wY, wZ].sbtype[wx, wy, wz] == 0) tree0[wX, wY, wZ].sbtype[wx, wy, wz] = 6;
                        }
                    }
                }
                /*for (byte vis=0;vis<6; vis++)
                {
                    sbyte lx = x;
                    sbyte ly = y;
                    sbyte lz = z;
                    for (byte rn = 0; rn < rand.Next(1,3); rn++)
                    {
                        lx += shifting[vis].X;
                        ly += shifting[vis].Y;
                        lz += shifting[vis].Z;
                        byte wx = (byte)(lx % 4);
                        byte wy = (byte)(ly % 4);
                        byte wz = (byte)(lz % 4);
                        byte wX = (byte)(lx / 4);
                        byte wY = (byte)(ly / 4);
                        byte wZ = (byte)(lz / 4);
                        if(tree0[wX, wY, wZ].stype[wx, wy, wz]==0) tree0[wX, wY, wZ].stype[wx, wy, wz] = 6;
                    }
                }*/
            }





            for (byte x = 0; x < 5; x++)
            {
                for (byte y = 0; y < 5; y++)
                {
                    for (byte z = 0; z < 6; z++)
                    {
                        if (tree0[x, y, z].type == 2)
                        for (byte sx = 0; sx < 4; sx++)
                        {
                            for (byte sy = 0; sy < 4; sy++)
                            {
                                for (sbyte sz = 3; sz >= 0; sz--)
                                {
                                    if (tree0[x, y, z].sbtype[sx, sy, sz] == 2)
                                    for (byte svis = 0; svis < 6; svis++)
                                    {
                                        sbyte workx = (sbyte)x;
                                        sbyte worky = (sbyte)y;
                                        sbyte workz = (sbyte)z;
                                        sbyte wsx = (sbyte)(sx + shifting[svis].X);
                                        sbyte wsy = (sbyte)(sy + shifting[svis].Y);
                                        sbyte wsz = (sbyte)(sz + shifting[svis].Z);
                                        if (wsx < 0 | wsx > 3)
                                        {
                                            wsx = (sbyte)((wsx + 4) % 4);//!!!!!!!!!!!!!!!!!!!!!!!
                                            workx = (sbyte)(x + shifting[svis].X);
                                            if (workx > 4 | workx < 0) { workx = (sbyte)x; wsx = (sbyte)(sx); }
                                        }
                                        if (wsy < 0 | wsy > 3)
                                        {
                                            wsy = (sbyte)((wsy + 4) % 4);
                                            worky = (sbyte)(y + (int)shifting[svis].Y);
                                            if (worky > 4 | worky < 0) { worky = (sbyte)y; wsy = (sbyte)(sy); }
                                        }
                                        if (wsz < 0 | wsz > 3)
                                        {
                                            wsz = (sbyte)((wsz + 4) % 4);
                                            workz = (sbyte)(z + (int)shifting[svis].Z);
                                            if (workz > 5 | workz < 0) { workz = (sbyte)z; wsz = (sbyte)(sz); }
                                        }
                                        //strmesh workblock = m[workX, workY].Map[workx, worky, workz];
                                        if (tree0[workx, worky, workz].type == 2) if (tree0[workx, worky, workz].sbtype[wsx, wsy, wsz] == 2) tree0[x, y, z].sbalt[sx, sy, sz, svis] = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return tree0;
        }
        private strmesh[, ,] tree(strmesh[, ,] tree0)
        {
            //strmesh[, ,] tree0 = new strmesh[5, 5, 6];
            for (byte sx = 0; sx < 5; sx++)
            {
                for (byte sy = 0; sy < 5; sy++)
                {
                    for (byte sz = 0; sz < 6; sz++)
                    {
                        if (tree0[sx, sy, sz].type == 0)
                        {
                            tree0[sx, sy, sz] = new strmesh(true);
                            tree0[sx, sy, sz].type = 2;
                            tree0[sx, sy, sz].bvis = true;
                        }
                    }
                }
            }
            byte startx = (byte)rand.Next(8, 12);
            byte starty = (byte)rand.Next(8, 12);
            byte fst = (byte)rand.Next(4, 8);
            for (byte st = 0; st < fst; st++)
            {
                sbyte x = (sbyte)(startx + rand.Next(-1, 1));
                sbyte y = (sbyte)(starty + rand.Next(-1, 1));
                sbyte z = -1;
                byte rando = (byte)rand.Next(2, 6);
                if (rando == 1) rando = 0;
                for (byte step = 0; step < 18; step++)
                {
                    byte rnd = (byte)rand.Next(2, 40);
                    if (rnd < 5 & z > 2)
                    {
                        x = (sbyte)(x + shifting[rnd].X);
                        if (x < 2) x = 2;
                        if (x > 17) x = 17;
                        y = (sbyte)(y + shifting[rnd].Y);
                        if (y < 2) y = 2;
                        if (y > 17) y = 17;
                        //z = (byte)(z + shifting[rnd].Z);
                    }
                    else if (rnd >= 5 & rnd < 10 & z > 2)
                    {
                        x = (sbyte)(x + shifting[rando].X);
                        if (x < 2) x = 2;
                        if (x > 17) x = 17;
                        y = (sbyte)(y + shifting[rando].Y);
                        if (y < 2) y = 2;
                        if (y > 17) y = 17;
                        //z = (byte)(z + shifting[rnd].Z);
                    }
                    else
                        z++;
                    byte wx = (byte)(x % 4);
                    byte wy = (byte)(y % 4);
                    byte wz = (byte)(z % 4);

                    byte wX = (byte)(x / 4);
                    byte wY = (byte)(y / 4);
                    byte wZ = (byte)(z / 4);
                    if (tree0[wX, wY, wZ].sb )if(tree0[wX, wY, wZ].sbtype[wx, wy, wz] == 0) tree0[wX, wY, wZ].sbtype[wx, wy, wz] = 2;
                    // tree0[wX, wY, wZ].sbvis[wx, wy, wz,0] = true;
                }

                byte lx = (byte)(x - rand.Next(0, 3));
                byte lfx = (byte)(x + rand.Next(0, 3));
                for (byte tx = lx; tx <= lfx; tx++)
                {
                    byte ly = (byte)(y - rand.Next(0, 3));
                    byte lfy = (byte)(y + rand.Next(0, 3));
                    for (byte ty = ly; ty <= lfy; ty++)
                    {
                        byte lz = (byte)(z - rand.Next(1, 3));
                        byte lfz = (byte)(z + rand.Next(1, 5));
                        for (byte tz = lz; tz <= lfz; tz++)
                        {
                            byte wx = (byte)(tx % 4);
                            byte wy = (byte)(ty % 4);
                            byte wz = (byte)(tz % 4);
                            byte wX = (byte)(tx / 4);
                            byte wY = (byte)(ty / 4);
                            byte wZ = (byte)(tz / 4);
                            if (tree0[wX, wY, wZ].sb) if (tree0[wX, wY, wZ].sbtype[wx, wy, wz] == 0) tree0[wX, wY, wZ].sbtype[wx, wy, wz] = 6;
                        }
                    }
                }
            }
            for (byte x = 0; x < 5; x++)
            {
                for (byte y = 0; y < 5; y++)
                {
                    for (byte z = 0; z < 6; z++)
                    {
                        if (tree0[x, y, z].type == 2)
                            for (byte sx = 0; sx < 4; sx++)
                            {
                                for (byte sy = 0; sy < 4; sy++)
                                {
                                    for (sbyte sz = 3; sz >= 0; sz--)
                                    {
                                        if (tree0[x, y, z].sbtype[sx, sy, sz] == 2)
                                            for (byte svis = 0; svis < 6; svis++)
                                            {
                                                sbyte workx = (sbyte)x;
                                                sbyte worky = (sbyte)y;
                                                sbyte workz = (sbyte)z;
                                                sbyte wsx = (sbyte)(sx + shifting[svis].X);
                                                sbyte wsy = (sbyte)(sy + shifting[svis].Y);
                                                sbyte wsz = (sbyte)(sz + shifting[svis].Z);
                                                if (wsx < 0 | wsx > 3)
                                                {
                                                    wsx = (sbyte)((wsx + 4) % 4);//!!!!!!!!!!!!!!!!!!!!!!!
                                                    workx = (sbyte)(x + shifting[svis].X);
                                                    if (workx > 4 | workx < 0) { workx = (sbyte)x; wsx = (sbyte)(sx); }
                                                }
                                                if (wsy < 0 | wsy > 3)
                                                {
                                                    wsy = (sbyte)((wsy + 4) % 4);
                                                    worky = (sbyte)(y + (int)shifting[svis].Y);
                                                    if (worky > 4 | worky < 0) { worky = (sbyte)y; wsy = (sbyte)(sy); }
                                                }
                                                if (wsz < 0 | wsz > 3)
                                                {
                                                    wsz = (sbyte)((wsz + 4) % 4);
                                                    workz = (sbyte)(z + (int)shifting[svis].Z);
                                                    if (workz > 5 | workz < 0) { workz = (sbyte)z; wsz = (sbyte)(sz); }
                                                }
                                                //strmesh workblock = m[workX, workY].Map[workx, worky, workz];
                                                if (tree0[workx, worky, workz].type == 2)  if (tree0[workx, worky, workz].sbtype[wsx, wsy, wsz] == 2) tree0[x, y, z].sbalt[sx, sy, sz, svis] = true;
                                            }
                                    }
                                }
                            }
                    }
                }
            }
            return tree0;
        }

        private MapList generatetestchunk(short numX, short numY, sbyte X, sbyte Y, bool work)
        {
            //sbyte[,] mapheight = new sbyte[64, 64];
            MapList generateM = new MapList(numX, numY);




            if (work)
            {
                for (int x = 0; x < 64; x++)
                {
                    for (int y = 0; y < 64; y++)
                    {
                        for (int z = 63; z >= 0; z--)
                        {

                            /*generateM.Map[x, y, z].svis = new bool[6];
                            generateM.Map[x, y, z].alt = new bool[6];
                            generateM.Map[x, y, z].taches = new byte[2,6];*/
                            // generateM.Map[x, y, z].taches = new List<string>(1);
                            generateM.Map[x, y, z] = new strmesh(false);

                            if (z > 15 & z < 20 & x<20 & y<20)
                            {
                                generateM.Map[x, y, z].type = 1;
                                generateM.Map[x, y, z].hill = objectlist[generateM.Map[x, y, z].type].standartHill;
                            }
                            else
                            {
                                generateM.Map[x, y, z] = new strmesh(false);
                                //generateM.Map[x, y, z].type = 0;
                                generateM.Map[x, y, z].taches[0, 0] = 20;
                                generateM.Map[x, y, z].taches[0, 1] = 20;
                                generateM.Map[x, y, z].taches[0, 2] = 20;
                                generateM.Map[x, y, z].taches[0, 3] = 20;
                                generateM.Map[x, y, z].taches[0, 4] = 20;
                                generateM.Map[x, y, z].taches[0, 5] = 20;
                            }
                            //generateM.Map[x, y, z].hill = objectlist[generateM.Map[x, y, z].type].standartHill;
                        }
                    }
                }
            }
            else
                for (int x = 0; x < 64; x++)
                {
                    for (int y = 0; y < 64; y++)
                    {
                        for (int z = 63; z >= 0; z--)
                        {

                            /*generateM.Map[x, y, z].svis = new bool[6];
                            generateM.Map[x, y, z].alt = new bool[6];
                            generateM.Map[x, y, z].taches = new byte[2,6];*/
                            // generateM.Map[x, y, z].taches = new List<string>(1);
                            generateM.Map[x, y, z] = new strmesh(false);
                        }
                    }
                }

         /*   for (byte trees = 0; trees < rand.Next(10, 40); trees++)
            {
                byte trex = (byte)rand.Next(5, 55);
                byte trey = (byte)rand.Next(5, 55);
                strmesh[, ,] tree0 = new strmesh[5, 5, 6];
                for (byte sx = 0; sx < 5; sx++)
                {
                    for (byte sy = 0; sy < 5; sy++)
                    {
                        for (byte sz = 0; sz < 6; sz++)
                        {
                            tree0[sx, sy, sz] = generateM.Map[trex + sx, trey + sy, height + mapheight[trex, trey] + 1 + sz];
                            //generateM.Map[trex + sx, trey + sy, height + mapheight[trex, trey] + 1 + sz] = tree0[sx, sy, sz];
                        }
                    }
                }
                tree0 = tree(tree0);
                for (byte sx = 0; sx < 5; sx++)
                {
                    for (byte sy = 0; sy < 5; sy++)
                    {
                        for (byte sz = 0; sz < 6; sz++)
                        {
                            generateM.Map[trex + sx, trey + sy, height + mapheight[trex, trey] + 1 + sz] = tree0[sx, sy, sz];
                        }
                    }
                }
            }
            */
            generateM.height = (byte)(20);

            return generateM;
        }
        private void тестовыйРежимToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            gametimer.Enabled = false;
            timer2.Enabled = false;
            int X0 = 0;
            int Y0 = 0;
            for (byte mapp = 0; mapp < 16; mapp++)
            {
                if(mapp==5) m[mapp / 4, mapp % 4] = generatetestchunk((short)(X0 + mapp / 4), (short)(Y0 + mapp % 4), (sbyte)(mapp / 4), (sbyte)(mapp % 4), true/*heightreg*/);
                else m[mapp / 4, mapp % 4] = generatetestchunk((short)(X0 + mapp / 4), (short)(Y0 + mapp % 4), (sbyte)(mapp / 4), (sbyte)(mapp % 4), false/*heightreg*/);
            }

            inventory[20, 0] = 1;
            inventory[20, 1] = 50;

            inventory[21, 0] = 5;
            inventory[21, 1] = 50;

            inventory[22, 0] = 3;
            inventory[22, 1] = 50;

            inventory[23, 0] = 4;
            inventory[23, 1] = 50;

            inventory[24, 0] = 4;
            inventory[24, 1] = 50;

            inventory[25, 0] = 100;
            inventory[25, 1] = (byte)objectlist[100].standartHill;

            inventory[26, 0] = 101;
            inventory[26, 1] = (byte)objectlist[101].standartHill;

            inventory[27, 0] = 102;
            inventory[27, 1] = (byte)objectlist[102].standartHill;

            inventory[28, 0] = 103;
            inventory[28, 1] = (byte)objectlist[103].standartHill;

            inventory[102, 0] = 104;
            inventory[102, 1] = (byte)objectlist[104].standartHill;

            inventory[101, 0] = 110;
            inventory[101, 1] = (byte)objectlist[110].standartHill;

            inventory[0, 0] = 8;
            inventory[0, 1] = 1;

            startgame();
        }
        private MapList generatechunk(short numX, short numY, sbyte X, sbyte Y, short height)
        {
            sbyte[,] mapheight = new sbyte[64, 64];
            MapList generateM = new MapList(numX, numY);

            for (byte n = 0; n < rand.Next(100, 200); n++)
            {
                int x0 = rand.Next(0, 64);
                int y0 = rand.Next(0, 64);
                sbyte genz = (sbyte)rand.Next(-1, 2);

                int xs = (x0 - 20 > 0) ? x0 - 20 : 0;
                int xf = (x0 + 20 < 64) ? x0 + 20 : 63;
                int ys = (y0 - 20 > 0) ? y0 - 20 : 0;
                int yf = (y0 + 20 < 64) ? y0 + 20 : 63;

                for (int x = xs; x < xf; x++)
                {
                    for (int y = ys; y < yf; y++)
                    {
                        mapheight[x, y] += (rand.Next(0, (int)Math.Sqrt(Math.Pow((x0 - x), 2) + Math.Pow((y0 - y), 2))) < 10) ? genz : (sbyte)0;
                    }
                }
            }
            for (int x = 0; x < 64; x++)
            {
                for (int y = 0; y < 64; y++)
                {
                    int nn = 0;
                    int summ = 0;
                    for (int xe = x - rand.Next(7, 10); xe < x + rand.Next(7, 10); xe++)
                    {
                        for (int ye = y - rand.Next(3, 20); ye < y + rand.Next(3, 20); ye++)
                        {
                            if (xe > 0 & xe < 64 & ye > 0 & ye < 64)
                            { summ += mapheight[xe, ye]; }
                            else
                            {
                                int aX = X;
                                int aY = Y;
                                if (xe < 0) aX--;
                                else if (xe >= 64) aX++;
                                if (ye < 0) aY--;
                                else if (ye >= 64) aY++;
                                if (aX >= 0 & aX < 4 & aY >= 0 & aY < 4)
                                {
                                    if (m[aX, aY].height > 0) summ += m[aX, aY].height - height + rand.Next(-1, 2);
                                    else summ += rand.Next(-1, 2);
                                }
                                else summ += rand.Next(-1, 2);
                            }
                            nn++;
                        }
                    }
                    mapheight[x, y] = (sbyte)(summ / nn);
                }
            }

            int numdirt = rand.Next(0, height / 3);
            for (int x = 0; x < 64; x++)
            {
                for (int y = 0; y < 64; y++)
                {
                    for (int z = 63; z >= 0; z--)
                    {

                        /*generateM.Map[x, y, z].svis = new bool[6];
                        generateM.Map[x, y, z].alt = new bool[6];
                        generateM.Map[x, y, z].taches = new byte[2,6];*/
                        // generateM.Map[x, y, z].taches = new List<string>(1);

                        if (z <= height + mapheight[x, y])
                        {
                            generateM.Map[x, y, z] = new strmesh((rand.Next(50) == 0) ? true : false);
                            //generateM.Map[x, y, z].hill = 1;
                            if (z == height + mapheight[x, y])
                            {

                                generateM.Map[x, y, z].type = 5;

                                //generateM.Map[x, y, z].sb = (rand.Next(50) == 0) ? true : false;

                                if (generateM.Map[x, y, z].sb)
                                {
                                    for (byte sbx = 0; sbx < 4; sbx++)
                                    {
                                        for (byte sby = 0; sby < 4; sby++)
                                        {
                                            for (byte sbz = 0; sbz < 4; sbz++)
                                            {
                                                if (sbz == 0 | (sbz == 1 & sbx == 1) | (sbz == 2 & sbx == 2) | (sbz == 3 & sbx == 3))
                                                {
                                                    generateM.Map[x, y, z].sbtype[sbx, sby, sbz] = 5;
                                                    generateM.Map[x, y, z].sbalt[sbx, sby, sbz, 0] = true;
                                                }
                                            }
                                        }
                                    }
                                }
                                else { generateM.Map[x, y, z].alt[0] = true; }
                            }
                            else if (z < height + mapheight[x, y] & z > height + mapheight[x, y] - rand.Next(numdirt - 2, numdirt + 2))
                            {
                                generateM.Map[x, y, z].type = 5;
                            }
                            else
                            {
                                generateM.Map[x, y, z].type = 3;
                            }
                            generateM.Map[x, y, z].hill = objectlist[generateM.Map[x, y, z].type].standartHill;

                        }
                        else
                        {
                            generateM.Map[x, y, z] = new strmesh(false);
                            //generateM.Map[x, y, z].type = 0;
                            generateM.Map[x, y, z].taches[0, 0] = 20;
                            generateM.Map[x, y, z].taches[0, 1] = 20;
                            generateM.Map[x, y, z].taches[0, 2] = 20;
                            generateM.Map[x, y, z].taches[0, 3] = 20;
                            generateM.Map[x, y, z].taches[0, 4] = 20;
                            generateM.Map[x, y, z].taches[0, 5] = 20;
                        }
                        //generateM.Map[x, y, z].hill = objectlist[generateM.Map[x, y, z].type].standartHill;
                    }
                }
            }

            for (byte water = 0; water < rand.Next(40, 60); water++)
            {
                byte watx = (byte)rand.Next(5, 55);
                byte waty = (byte)rand.Next(5, 55);
                if (mapheight[watx, waty]<-1)
                {
                    byte watz = (byte)(height + mapheight[watx, waty] - 5);
                    for (byte sz = (byte)(watz + 1); sz <= height + mapheight[watx, waty]; sz++)
                    {
                        generateM.Map[watx, waty, sz] = new strmesh(false);
                        for (byte sh = 0; sh < 6; sh++)
                        { generateM.Map[watx, waty, sz].taches[0, sh] = 20; }
                    }
                    generateM.Map[watx, waty, watz] = new strmesh(false);
                    generateM.Map[watx, waty, watz].type = 8;

                    generateM.Map[watx, waty, watz].taches[2, 0] = 6 * 4;
                    for (byte sh = 1; sh < 6; sh++)
                    {
                        generateM.Map[watx, waty, watz].taches[2, sh] = 250;
                    }
                }
            }

                for (byte trees = 0; trees < rand.Next(10, 40); trees++)
                {
                    byte trex = (byte)rand.Next(5, 55);
                    byte trey = (byte)rand.Next(5, 55);
                    if (mapheight[trex, trey] >= -1)
                    {
                        strmesh[, ,] tree0 = new strmesh[5, 5, 6];
                        for (byte sx = 0; sx < 5; sx++)
                        {
                            for (byte sy = 0; sy < 5; sy++)
                            {
                                for (byte sz = 0; sz < 6; sz++)
                                {
                                    tree0[sx, sy, sz] = generateM.Map[trex + sx, trey + sy, height + mapheight[trex, trey] + 1 + sz];
                                    //generateM.Map[trex + sx, trey + sy, height + mapheight[trex, trey] + 1 + sz] = tree0[sx, sy, sz];
                                }
                            }
                        }
                        tree0 = tree(tree0);
                        for (byte sx = 0; sx < 5; sx++)
                        {
                            for (byte sy = 0; sy < 5; sy++)
                            {
                                for (byte sz = 0; sz < 6; sz++)
                                {
                                    generateM.Map[trex + sx, trey + sy, height + mapheight[trex, trey] + 1 + sz] = tree0[sx, sy, sz];
                                }
                            }
                        }
                    }
                }

            int heightsum = 0;
            for (byte x = 0; x < 64; x++)
            {
                for (byte y = 0; y < 64; y++)
                {
                    heightsum += (height + mapheight[x, y]);
                }
            }
            generateM.height = (byte)(heightsum / (64 * 64));

            return generateM;
        }
        private void новыйМирToolStripMenuItem_Click(object sender, EventArgs e)
        {
            gametimer.Enabled = false;
            timer2.Enabled = false;

            int X0 = rand.Next(-100, 101);
            int Y0 = rand.Next(-100, 101);
            for (byte mapp = 0; mapp < 16; mapp++)
            {
                m[mapp / 4, mapp % 4] = generatechunk((short)(X0 + mapp / 4),(short)(Y0 + mapp % 4), (sbyte)(mapp / 4), (sbyte)(mapp % 4), (short)hi(mapp / 4, mapp % 4)/*heightreg*/);
            }
            for (byte st = 0; st < 2; st++)
            for (byte mapp = 0; mapp < 16; mapp++)   ///ПРОСЧЁТ ВИДИМОСТИ УЧАСТКОВ
            {
                m[mapp / 4, mapp % 4] = checkvisiblechunk(m, (sbyte)mapp / 4, (sbyte)mapp % 4);

                for (byte x = 0; x < 64; x++)
                {
                    for (byte y = 0; y < 64; y++)
                    {
                        for (sbyte z = 63; z >= 0; z--)
                        {
                            m[mapp / 4, mapp % 4].Map[x, y, z] = checkblock((byte)(mapp / 4), (byte)(mapp % 4), x, y, (byte)z, true);
                            //m[mapp / 4, mapp % 4].Map[x, y, z] = checktatches(m, mapp / 4, mapp % 4, x, y, z);
                        }
                    }
                }
            }
            for (byte i = 0; i < 105; i++)
            {
                inventory[i, 0] = 0;
                inventory[i, 1] = 0;
            }
            startgame();
        }
        public Form1()
        {
            InitializeComponent();
            this.MouseWheel += new MouseEventHandler(Form1_MouseWheel);
            going = false;
        }
        public bool InicLow()
        {
            try
            {
                param.Windowed = true;
                param.SwapEffect = SwapEffect.Discard;
                param.EnableAutoDepthStencil = true;
                param.AutoDepthStencilFormat = DepthFormat.D16;
                param.MultiSample = MultiSampleType.None;
                dev = new Device(0, DeviceType.Hardware, this, CreateFlags.SoftwareVertexProcessing, param);
                return true;
            }
            catch
            {
                return false;
            }
        }
        public bool InicHigh()
        {
            try
            {
                param.Windowed = true;
                param.SwapEffect = SwapEffect.Discard;
                param.EnableAutoDepthStencil = true;
                param.AutoDepthStencilFormat = DepthFormat.D24S8;
                param.MultiSample = MultiSampleType.FourSamples;
                dev = new Device(0, DeviceType.Hardware, this, CreateFlags.HardwareVertexProcessing, param);
                return true;
            }
            catch
            {
                return false;
            }

        }
        public void setobject()
        {
            string s;

            objectlist[0].type = "Gas block";
            objectlist[0].name = "Кислород";
            objectlist[0].stacksize = 0;

            objectlist[1].type = "block";
            objectlist[1].name = "Песок";
            objectlist[1].stacksize = 64;
            objectlist[1].standartHill = 200;
            // objectlist[1].blockKode = 1;
            s = Application.StartupPath + "\\boxsand.png";
            itx = Image.FromFile(s);
            //   if (tx != null) tx.Dispose();
            objectlist[1].texinv = TextureLoader.FromFile(dev, s, itx.Width, itx.Height, 1, Usage.None, Format.A8B8G8R8, Pool.Managed, Filter.None, Filter.None, Color.FromArgb(255, 0, 0, 0).ToArgb());
            s = Application.StartupPath.ToString() + "\\Texture1.png";
            ima = Image.FromFile(s);
            objectlist[1].texworld = TextureLoader.FromFile(dev, s);

            objectlist[2].type = "block";
            objectlist[2].name = "Древесина";
            objectlist[2].stacksize = 64;
            objectlist[2].standartHill = 250;
            // objectlist[2].blockKode = 2;
            s = Application.StartupPath + "\\boxgrass.png";
            itx = Image.FromFile(s);
            //   if (tx != null) tx.Dispose();
            objectlist[2].texinv = TextureLoader.FromFile(dev, s, itx.Width, itx.Height, 1, Usage.None, Format.A8B8G8R8, Pool.Managed, Filter.None, Filter.None, Color.FromArgb(255, 0, 0, 0).ToArgb());
            s = Application.StartupPath.ToString() + "\\Texture2.png";
            ima = Image.FromFile(s);
            objectlist[2].texworld = TextureLoader.FromFile(dev, s);
            s = Application.StartupPath.ToString() + "\\Texture8.png";
            ima = Image.FromFile(s);
            objectlist[2].alttexworld = TextureLoader.FromFile(dev, s);

            objectlist[3].type = "block";
            objectlist[3].name = "Камень";
            objectlist[3].stacksize = 64;
            objectlist[3].standartHill = 400;
            //  objectlist[3].blockKode = 3;
            s = Application.StartupPath + "\\boxstone.png";
            itx = Image.FromFile(s);
            //   if (tx != null) tx.Dispose();
            objectlist[3].texinv = TextureLoader.FromFile(dev, s, itx.Width, itx.Height, 1, Usage.None, Format.A8B8G8R8, Pool.Managed, Filter.None, Filter.None, Color.FromArgb(255, 0, 0, 0).ToArgb());
            s = Application.StartupPath.ToString() + "\\Texture3.png";
            ima = Image.FromFile(s);
            objectlist[3].texworld = TextureLoader.FromFile(dev, s);

            objectlist[4].type = "block";
            objectlist[4].name = "Кирпичи";
            objectlist[4].stacksize = 64;
            objectlist[4].standartHill = 500;
            // objectlist[4].blockKode = 4;
            s = Application.StartupPath + "\\boxk.png";
            itx = Image.FromFile(s);
            //   if (tx != null) tx.Dispose();
            objectlist[4].texinv = TextureLoader.FromFile(dev, s, itx.Width, itx.Height, 1, Usage.None, Format.A8B8G8R8, Pool.Managed, Filter.None, Filter.None, Color.FromArgb(255, 0, 0, 0).ToArgb());
            s = Application.StartupPath.ToString() + "\\Texture4.png";
            ima = Image.FromFile(s);
            objectlist[4].texworld = TextureLoader.FromFile(dev, s);

            objectlist[5].type = "block";
            objectlist[5].name = "Земля";
            objectlist[5].stacksize = 64;
            objectlist[5].standartHill = 220;
            // objectlist[4].blockKode = 4;
            s = Application.StartupPath + "\\boxdirt.png";
            itx = Image.FromFile(s);
            //   if (tx != null) tx.Dispose();
            objectlist[5].texinv = TextureLoader.FromFile(dev, s, itx.Width, itx.Height, 1, Usage.None, Format.A8B8G8R8, Pool.Managed, Filter.None, Filter.None, Color.FromArgb(255, 0, 0, 0).ToArgb());
            s = Application.StartupPath.ToString() + "\\Texture5.png";
            ima = Image.FromFile(s);
            objectlist[5].texworld = TextureLoader.FromFile(dev, s);
            s = Application.StartupPath.ToString() + "\\Texture6.png";
            ima = Image.FromFile(s);
            objectlist[5].alttexworld = TextureLoader.FromFile(dev, s);

            objectlist[6].type = "block";
            objectlist[6].name = "Листья";
            objectlist[6].stacksize = 64;
            objectlist[6].standartHill = 100;
            // objectlist[4].blockKode = 4;
            s = Application.StartupPath + "\\boxdirt.png";
            itx = Image.FromFile(s);
            //   if (tx != null) tx.Dispose();
            objectlist[6].texinv = TextureLoader.FromFile(dev, s, itx.Width, itx.Height, 1, Usage.None, Format.A8B8G8R8, Pool.Managed, Filter.None, Filter.None, Color.FromArgb(255, 0, 0, 0).ToArgb());
            s = Application.StartupPath.ToString() + "\\Texture7.png";
            ima = Image.FromFile(s);
            objectlist[6].texworld = TextureLoader.FromFile(dev, s);

            objectlist[7].type = "block";
            objectlist[7].name = "Источник света";
            objectlist[7].stacksize = 64;
            objectlist[7].standartHill = 10;
            // objectlist[4].blockKode = 4;
            s = Application.StartupPath + "\\boxdirt.png";
            itx = Image.FromFile(s);
            //   if (tx != null) tx.Dispose();
            objectlist[7].texinv = TextureLoader.FromFile(dev, s, itx.Width, itx.Height, 1, Usage.None, Format.A8B8G8R8, Pool.Managed, Filter.None, Filter.None, Color.FromArgb(255, 0, 0, 0).ToArgb());
            s = Application.StartupPath.ToString() + "\\Texture5.png";
            ima = Image.FromFile(s);
            objectlist[7].texworld = TextureLoader.FromFile(dev, s);
            s = Application.StartupPath.ToString() + "\\Texture6.png";
            ima = Image.FromFile(s);
            objectlist[7].alttexworld = TextureLoader.FromFile(dev, s);

            objectlist[8].type = "liguid block";
            objectlist[8].name = "Вода";
            objectlist[8].stacksize = 64;
            objectlist[8].standartHill = 10;
            // objectlist[4].blockKode = 4;
            s = Application.StartupPath + "\\boxdirt.png";
            itx = Image.FromFile(s);
            //   if (tx != null) tx.Dispose();
            objectlist[8].texinv = TextureLoader.FromFile(dev, s, itx.Width, itx.Height, 1, Usage.None, Format.A8B8G8R8, Pool.Managed, Filter.None, Filter.None, Color.FromArgb(255, 0, 0, 0).ToArgb());
            s = Application.StartupPath.ToString() + "\\Texture9.png";
            ima = Image.FromFile(s);
            objectlist[8].texworld = TextureLoader.FromFile(dev, s);
            s = Application.StartupPath.ToString() + "\\Texture9.png";
            ima = Image.FromFile(s);
            objectlist[8].alttexworld = TextureLoader.FromFile(dev, s);

            objectlist[9].type = "block";
            objectlist[9].name = "Снег";
            objectlist[9].stacksize = 64;
            objectlist[9].standartHill = 10;
            // objectlist[4].blockKode = 4;
            s = Application.StartupPath + "\\boxdirt.png";
            itx = Image.FromFile(s);
            //   if (tx != null) tx.Dispose();
            objectlist[9].texinv = TextureLoader.FromFile(dev, s, itx.Width, itx.Height, 1, Usage.None, Format.A8B8G8R8, Pool.Managed, Filter.None, Filter.None, Color.FromArgb(255, 0, 0, 0).ToArgb());
            s = Application.StartupPath.ToString() + "\\Texture10.png";
            ima = Image.FromFile(s);
            objectlist[9].texworld = TextureLoader.FromFile(dev, s);
            s = Application.StartupPath.ToString() + "\\Texture10.png";
            ima = Image.FromFile(s);
            objectlist[9].alttexworld = TextureLoader.FromFile(dev, s);

            objectlist[100].type = "p";
            objectlist[100].name = "p21";
            objectlist[100].stacksize = 1;
            objectlist[100].p1 = 2;
            objectlist[100].p2 = 1;
            objectlist[100].standartHill = 100;
            s = Application.StartupPath + "\\p21.png";
            itx = Image.FromFile(s);
            objectlist[100].texinv = TextureLoader.FromFile(dev, s, itx.Width, itx.Height, 1, Usage.None, Format.A8B8G8R8, Pool.Managed, Filter.None, Filter.None, Color.FromArgb(255, 0, 0, 0).ToArgb());
            //  s = Application.StartupPath.ToString() + "\\Texture5.png";
            //  ima = Image.FromFile(s);
            //  objectlist[5].texworld = TextureLoader.FromFile(dev, s);

            objectlist[101].type = "p";
            objectlist[101].name = "p51";
            objectlist[101].stacksize = 1;
            objectlist[101].p1 = 5;
            objectlist[101].p2 = 1;
            objectlist[101].standartHill = 150;
            s = Application.StartupPath + "\\p51.png";
            itx = Image.FromFile(s);
            objectlist[101].texinv = TextureLoader.FromFile(dev, s, itx.Width, itx.Height, 1, Usage.None, Format.A8B8G8R8, Pool.Managed, Filter.None, Filter.None, Color.FromArgb(255, 0, 0, 0).ToArgb());
            //  s = Application.StartupPath.ToString() + "\\Texture5.png";
            //ima = Image.FromFile(s);
            //   objectlist[5].texworld = TextureLoader.FromFile(dev, s);

            objectlist[102].type = "p";
            objectlist[102].name = "p52";
            objectlist[102].stacksize = 1;
            objectlist[102].p1 = 5;
            objectlist[102].p2 = 2;
            objectlist[102].standartHill = 150;
            s = Application.StartupPath + "\\p52.png";
            itx = Image.FromFile(s);
            objectlist[102].texinv = TextureLoader.FromFile(dev, s, itx.Width, itx.Height, 1, Usage.None, Format.A8B8G8R8, Pool.Managed, Filter.None, Filter.None, Color.FromArgb(255, 0, 0, 0).ToArgb());
            //  s = Application.StartupPath.ToString() + "\\Texture5.png";
            //ima = Image.FromFile(s);
            //   objectlist[5].texworld = TextureLoader.FromFile(dev, s);

            objectlist[103].type = "p";
            objectlist[103].name = "p55";
            objectlist[103].stacksize = 1;
            objectlist[103].p1 = 5;
            objectlist[103].p2 = 5;
            objectlist[103].standartHill = 150;
            s = Application.StartupPath + "\\p55.png";
            itx = Image.FromFile(s);
            objectlist[103].texinv = TextureLoader.FromFile(dev, s, itx.Width, itx.Height, 1, Usage.None, Format.A8B8G8R8, Pool.Managed, Filter.None, Filter.None, Color.FromArgb(255, 0, 0, 0).ToArgb());
            //  s = Application.StartupPath.ToString() + "\\Texture5.png";
            //ima = Image.FromFile(s);
            //   objectlist[5].texworld = TextureLoader.FromFile(dev, s);

            objectlist[104].type = "p";
            objectlist[104].name = "p1010";
            objectlist[104].stacksize = 1;
            objectlist[104].p1 = 10;
            objectlist[104].p2 = 10;
            objectlist[104].standartHill = 250;
            s = Application.StartupPath + "\\p1010.png";
            itx = Image.FromFile(s);
            objectlist[104].texinv = TextureLoader.FromFile(dev, s, itx.Width, itx.Height, 1, Usage.None, Format.A8B8G8R8, Pool.Managed, Filter.None, Filter.None, Color.FromArgb(255, 0, 0, 0).ToArgb());
            //  s = Application.StartupPath.ToString() + "\\Texture5.png";
            //ima = Image.FromFile(s);
            //   objectlist[5].texworld = TextureLoader.FromFile(dev, s);

            objectlist[110].type = "topclothe";
            objectlist[110].name = "Куртка";
            objectlist[110].stacksize = 1;
            objectlist[110].p1 = 5;
            objectlist[110].p2 = 5;
            objectlist[110].standartHill = 50;
            s = Application.StartupPath + "\\r05.png";
            itx = Image.FromFile(s);
            objectlist[110].texinv = TextureLoader.FromFile(dev, s, itx.Width, itx.Height, 1, Usage.None, Format.A8B8G8R8, Pool.Managed, Filter.None, Filter.None, Color.FromArgb(255, 0, 0, 0).ToArgb());
            //  s = Application.StartupPath.ToString() + "\\Texture5.png";
            //ima = Image.FromFile(s);
            //   objectlist[5].texworld = TextureLoader.FromFile(dev, s);
            s = Application.StartupPath + "\\toolrepair.png";
            itx = Image.FromFile(s);
            toolrepair = TextureLoader.FromFile(dev, s, itx.Width, itx.Height, 1, Usage.None, Format.A8B8G8R8, Pool.Managed, Filter.None, Filter.None, Color.FromArgb(255, 0, 0, 0).ToArgb());
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            if (!InicHigh())
            {
                if (!InicLow())
                {
                    MessageBox.Show("Impossible");
                    this.Close();
                }
            }
            dev.DeviceResizing += new CancelEventHandler(dev_DeviceResizing);

            setobject();

            /////////////////////////////////////
            shifting[0] = new bvec3(0, 0, 1);
            shifting[1] = new bvec3(0, 0, -1);
            shifting[2] = new bvec3(0, -1, 0);
            shifting[3] = new bvec3(0, 1, 0);
            shifting[4] = new bvec3(-1, 0, 0);
            shifting[5] = new bvec3(1, 0, 0);
            shifting[6] = new bvec3(0, 0, 0);

            ool[0] = new bvec2(1, 1);
            ool[1] = new bvec2(2, 2);
            ool[2] = new bvec2(1, 2);
            ool[3] = new bvec2(2, 1);

            ool[4] = new bvec2(0, 1);
            ool[5] = new bvec2(3, 2);
            ool[6] = new bvec2(1, 0);
            ool[7] = new bvec2(2, 3);
            ool[8] = new bvec2(3, 1);
            ool[9] = new bvec2(0, 2);
            ool[10] = new bvec2(2, 0);
            ool[11] = new bvec2(1, 3);

            ool[12] = new bvec2(0, 0);
            ool[13] = new bvec2(3, 3);
            ool[14] = new bvec2(0, 3);
            ool[15] = new bvec2(3, 0);

            //   nqv = nq + 1;
            // lq = sq * nq;
            vbplane = new VertexBuffer(typeof(CustomVertex.PositionNormalTextured), 42, dev, Usage.WriteOnly, VertexFormats.PositionNormal | VertexFormats.Texture1/*VertexFormats.PositionNormal/*CustomVertex.PositionNormal.Format*/, Pool.Default);
            ibplane = new IndexBuffer(typeof(int), 108, dev, Usage.WriteOnly, Pool.Default);



            newdata();
            newindex();
            buff();
            cp = new Vector3(60, 60, 300);
            //cp.Z = 300;
            ct = cp + Vector3.Normalize(new Vector3(1, 1, 0));

            LIN = new Line(dev);

            //Свойства источников
            /*  dev.Lights[0].Type = LightType.Spot;
              dev.Lights[0].Diffuse = Color.PaleGoldenrod;
              dev.Lights[0].Attenuation1 = 0.001f;
              dev.Lights[0].Falloff = 1;
              dev.Lights[0].Range = 2000;
              dev.Lights[0].InnerConeAngle = 3f;
              dev.Lights[0].OuterConeAngle = 3.14f;*/
            dev.Lights[0].Type = LightType.Directional;
            dev.Lights[0].Diffuse = Color.White;

            /*    dev.Lights[1].Type = LightType.Spot;
               dev.Lights[1].Diffuse = Color.PaleGoldenrod;
               dev.Lights[1].Attenuation1 = 0.005f;
               dev.Lights[1].Falloff = 10;
               dev.Lights[1].Range = 200;
               dev.Lights[1].InnerConeAngle = 0.8f;
               dev.Lights[1].OuterConeAngle = 2;*/
            
        }
        private void startgame()
        {
            try { t0.Abort(); }
            catch { }
            gametimer.Interval = 1000 / cps;
            timer2.Interval = 250;//четыре раза в секунду
            gametimer.Enabled = true;
            timer2.Enabled = true;
            Cursor.Position = center();
            going = true;
            Cursor.Hide();
            menuStrip1.Visible = false;
            panel1.Visible = false;
            panel1.Enabled = false;
            HighRes_Butt.Enabled = false;
            HighRes_Butt.Visible = false;
            trackBar1.Enabled = false;
            setinvcoord();
            ThreadStart ts0 = new ThreadStart(shiftchunks);
            t0 = new Thread(ts0);
            t0.Start();
        }
        void dev_DeviceResizing(object sender, CancelEventArgs e)
        {
            buff();
        }
        private void setinvcoord()
        {  //coeffwidth = this.Width / 40;

                if (inventory[101, 0] != 0)
                {
                    rsize = objectlist[inventory[101, 0]].p1;
                    //leftsize = objectlist[inventory[102, 0]].p2;
                }
                else
                {
                    rsize = 0;
                    //leftsize = 1;
                }
                for (byte inv = 80; inv < 100; inv++) if (inv > 79 + rsize & inventory[inv, 0] != 0) { inventory[inv, 0] = 0; inventory[inv, 1] = 0; }
                //   setinvcoord();
            
            
                if (inventory[102, 0] != 0)
                {
                    rightsize = objectlist[inventory[102, 0]].p1;
                    leftsize = objectlist[inventory[102, 0]].p2;
                }
                else
                {
                    rightsize = 1;
                    leftsize = 1;
                }
                for (byte inv = 0; inv < 20; inv++) if (((inv > rightsize - 1 & inv < 10) | (inv > leftsize + 9 & inv < 20)) & inventory[inv, 0] != 0) { inventory[inv, 0] = 0; inventory[inv, 1] = 0; }




            for (int numinv = 0; numinv < rightsize; numinv++) { handinvcoord[numinv] = new Point(this.Width - (coeffwidth + 5) * rightsize + (coeffwidth + 5) * numinv, this.Height - 40 - (coeffwidth + 5) * numinv + numinv * numinv * 3); }

            for (int numinv = 0; numinv < leftsize; numinv++) { handinvcoord[numinv + 10] = new Point((coeffwidth + 5) * leftsize - (coeffwidth + 5) * numinv, this.Height - 40 - (coeffwidth + 5) * numinv + numinv * numinv * 3); }

            for (int ui = 0; ui < 10; ui++) { invcoord[ui] = new Point(this.Width / 2 + (ui + 2) * (coeffwidth + 3), this.Height / 2 + (int)(coeffwidth * 7.5)); }

            for (int ui = 0; ui < 10; ui++) { invcoord[ui + 10] = new Point(this.Width / 2 - (ui + 1) * (coeffwidth + 3), this.Height / 2 + (int)(coeffwidth * 7.5)); }

            for (int ui = 0; ui < 60; ui++) { invcoord[ui + 20] = new Point(this.Width / 2 + (-1 + ui / 10) * (coeffwidth + 3), this.Height / 2 + (-4 + ui % 10) * (coeffwidth + 3)); }

            for (int ui = 0; ui < 5; ui++) { invcoord[ui + 100] = new Point(this.Width / 2 - 6 * (coeffwidth + 3), this.Height / 2 - (coeffwidth + 3) * 2 + ui * (coeffwidth + 3)); }
            //rsize = 15;
            for (int ui = 0; ui < 20; ui++) { invcoord[ui + 80] = new Point(this.Width / 2 - 4 * (coeffwidth + 3) + ui / 5 * (coeffwidth + 3), this.Height / 2 - (int)((coeffwidth + 3) * 2) + ui % 5 * (coeffwidth + 3)); }
        }
        private void buff()
        {
            GraphicsStream gs = vbplane.Lock(0, 0, LockFlags.None);
            gs.Write(dp);
            vbplane.Unlock();
            gs.Dispose();

            GraphicsStream gsi = ibplane.Lock(0, 0, LockFlags.None);
            gsi.Write(di);
            ibplane.Unlock();
            gsi.Dispose();

            //перекрестие
            vec[0] = new Vector2(this.Width / 2, this.Height / 2 - 10);
            vec[1] = new Vector2(this.Width / 2 + 10, this.Height / 2);
            vec[2] = new Vector2(this.Width / 2, this.Height / 2 + 10);
            vec[3] = new Vector2(this.Width / 2 - 10, this.Height / 2);
            vec[4] = vec[0];

            setinvcoord();
        }
        private int hi(int x, int y)
        {
            int heightreg = 0;
            int n = 0;
            for (int sm = 0; sm < 9; sm++)
            {
                int aX = x + sm / 3 - 1;
                int aY = y + sm % 3 - 1;
                if (aX >= 0 & aX < 4 & aY >= 0 & aY < 4)
                {
                    if (m[aX, aY].height > 0)
                    {
                        heightreg += m[aX, aY].height;
                        n++;
                    }
                }
            }
            if (n > 0)
                return heightreg / n;
            else
                return rand.Next(20, 40);
        }
        private void newdata()
        {
            dp = new CustomVertex.PositionNormalTextured[42];

            dp[0] = new CustomVertex.PositionNormalTextured(0, 8, 8, 0, 0, 16, 0.3333f, 0);
            dp[1] = new CustomVertex.PositionNormalTextured(8, 8, 8, 0, 0, 16, 0.6666f, 0);
            dp[2] = new CustomVertex.PositionNormalTextured(0, 8, 8, 0, 0, 16, 0, 0.25f);
            dp[3] = new CustomVertex.PositionNormalTextured(0, 0, 8, 0, 0, 16, 0.3333f, 0.25f);
            dp[4] = new CustomVertex.PositionNormalTextured(8, 0, 8, 0, 0, 16, 0.6666f, 0.25f);
            dp[5] = new CustomVertex.PositionNormalTextured(8, 8, 8, 0, 0, 16, 1f, 0.25f);
            dp[6] = new CustomVertex.PositionNormalTextured(0, 8, 0, 0, 0, 16, 0, 0.5f);
            dp[7] = new CustomVertex.PositionNormalTextured(0, 0, 0, 0, 0, 16, 0.3333f, 0.5f);
            dp[8] = new CustomVertex.PositionNormalTextured(8, 0, 0, 0, 0, 16, 0.6666f, 0.5f);
            dp[9] = new CustomVertex.PositionNormalTextured(8, 8, 0, 0, 0, 16, 1f, 0.5f);
            dp[10] = new CustomVertex.PositionNormalTextured(0, 8, 0, 0, 0, 16, 0.3333f, 0.75f);
            dp[11] = new CustomVertex.PositionNormalTextured(8, 8, 0, 0, 0, 16, 0.6666f, 0.75f);
            dp[12] = new CustomVertex.PositionNormalTextured(0, 8, 8, 0, 0, 16, 0.3333f, 1);
            dp[13] = new CustomVertex.PositionNormalTextured(8, 8, 8, 0, 0, 16, 0.6666f, 1);

            dp[14] = new CustomVertex.PositionNormalTextured(0, 2, 2, 0, 0, 16, 0.0833f, 0);
            dp[15] = new CustomVertex.PositionNormalTextured(2, 2, 2, 0, 0, 16, 0.1666f, 0);
            dp[16] = new CustomVertex.PositionNormalTextured(0, 2, 2, 0, 0, 16, 0, 0.0625f);
            dp[17] = new CustomVertex.PositionNormalTextured(0, 0, 2, 0, 0, 16, 0.0833f, 0.0625f);
            dp[18] = new CustomVertex.PositionNormalTextured(2, 0, 2, 0, 0, 16, 0.1666f, 0.0625f);
            dp[19] = new CustomVertex.PositionNormalTextured(2, 2, 2, 0, 0, 16, 0.25f, 0.0625f);
            dp[20] = new CustomVertex.PositionNormalTextured(0, 2, 0, 0, 0, 16, 0, 0.125f);
            dp[21] = new CustomVertex.PositionNormalTextured(0, 0, 0, 0, 0, 16, 0.0833f, 0.125f);
            dp[22] = new CustomVertex.PositionNormalTextured(2, 0, 0, 0, 0, 16, 0.1666f, 0.125f);
            dp[23] = new CustomVertex.PositionNormalTextured(2, 2, 0, 0, 0, 16, 0.25f, 0.125f);
            dp[24] = new CustomVertex.PositionNormalTextured(0, 2, 0, 0, 0, 16, 0.0833f, 0.1875f);
            dp[25] = new CustomVertex.PositionNormalTextured(2, 2, 0, 0, 0, 16, 0.1666f, 0.1875f);
            dp[26] = new CustomVertex.PositionNormalTextured(0, 2, 2, 0, 0, 16, 0.0833f, 0.25f);
            dp[27] = new CustomVertex.PositionNormalTextured(2, 2, 2, 0, 0, 16, 0.1666f, 0.25f);

            dp[28] = new CustomVertex.PositionNormalTextured(0, 1, 1, 0, 0, 16, 0.3333f, 0);
            dp[29] = new CustomVertex.PositionNormalTextured(1, 1, 1, 0, 0, 16, 0.6666f, 0);
            dp[30] = new CustomVertex.PositionNormalTextured(0, 1, 1, 0, 0, 16, 0, 0.25f);
            dp[31] = new CustomVertex.PositionNormalTextured(0, 0, 1, 0, 0, 16, 0.3333f, 0.25f);
            dp[32] = new CustomVertex.PositionNormalTextured(1, 0, 1, 0, 0, 16, 0.6666f, 0.25f);
            dp[33] = new CustomVertex.PositionNormalTextured(1, 1, 1, 0, 0, 16, 1f, 0.25f);
            dp[34] = new CustomVertex.PositionNormalTextured(0, 1, 0, 0, 0, 16, 0, 0.5f);
            dp[35] = new CustomVertex.PositionNormalTextured(0, 0, 0, 0, 0, 16, 0.3333f, 0.5f);
            dp[36] = new CustomVertex.PositionNormalTextured(1, 0, 0, 0, 0, 16, 0.6666f, 0.5f);
            dp[37] = new CustomVertex.PositionNormalTextured(1, 1, 0, 0, 0, 16, 1f, 0.5f);
            dp[38] = new CustomVertex.PositionNormalTextured(0, 1, 0, 0, 0, 16, 0.3333f, 0.75f);
            dp[39] = new CustomVertex.PositionNormalTextured(1, 1, 0, 0, 0, 16, 0.6666f, 0.75f);
            dp[40] = new CustomVertex.PositionNormalTextured(0, 1, 1, 0, 0, 16, 0.3333f, 1);
            dp[41] = new CustomVertex.PositionNormalTextured(1, 1, 1, 0, 0, 16, 0.6666f, 1);


            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    for (int z = 0; z < 8; z++)
                    { smallboxcoord[x, y, z] = new bvec3((sbyte)(x * 2), (sbyte)(y * 2), (sbyte)(z * 2)); }
                }
            }

            for (int x = 0; x < 64; x++)
            {
                for (int y = 0; y < 64; y++)
                {
                    for (int z = 0; z < 64; z++)
                    {
                        boxcoord[x, y, z] = new svec3((short)(x * 8), (short)(y * 8), (short)(z * 8));

                    }
                }
            }
        }
        private void newindex()
        {
            di[0] = 0;
            di[1] = 1;
            di[2] = 3;
            di[3] = 1;
            di[4] = 3;
            di[5] = 4;
            di[6] = 7;
            di[7] = 8;
            di[8] = 10;
            di[9] = 8;
            di[10] = 10;
            di[11] = 11;
            di[12] = 3;
            di[13] = 4;
            di[14] = 7;
            di[15] = 4;
            di[16] = 7;
            di[17] = 8;
            di[18] = 10;
            di[19] = 11;
            di[20] = 12;
            di[21] = 11;
            di[22] = 12;
            di[23] = 13;
            di[24] = 2;
            di[25] = 3;
            di[26] = 6;
            di[27] = 3;
            di[28] = 6;
            di[29] = 7;
            di[30] = 4;
            di[31] = 5;
            di[32] = 8;
            di[33] = 5;
            di[34] = 8;
            di[35] = 9;

            di[36] = 14;
            di[37] = 15;
            di[38] = 17;
            di[39] = 15;
            di[40] = 17;
            di[41] = 18;
            di[42] = 21;
            di[43] = 22;
            di[44] = 24;
            di[45] = 22;
            di[46] = 24;
            di[47] = 25;
            di[48] = 17;
            di[49] = 18;
            di[50] = 21;
            di[51] = 18;
            di[52] = 21;
            di[53] = 22;
            di[54] = 24;
            di[55] = 25;
            di[56] = 26;
            di[57] = 25;
            di[58] = 26;
            di[59] = 27;
            di[60] = 16;
            di[61] = 17;
            di[62] = 20;
            di[63] = 17;
            di[64] = 20;
            di[65] = 21;
            di[66] = 18;
            di[67] = 19;
            di[68] = 22;
            di[69] = 19;
            di[70] = 22;
            di[71] = 23;

            di[72] = 28;
            di[73] = 29;
            di[74] = 31;
            di[75] = 29;
            di[76] = 31;
            di[77] = 32;
            di[78] = 35;
            di[79] = 36;
            di[80] = 38;
            di[81] = 36;
            di[82] = 38;
            di[83] = 39;
            di[84] = 31;
            di[85] = 32;
            di[86] = 35;
            di[87] = 32;
            di[88] = 35;
            di[89] = 36;
            di[90] = 38;
            di[91] = 39;
            di[92] = 40;
            di[93] = 39;
            di[94] = 40;
            di[95] = 41;
            di[96] = 30;
            di[97] = 31;
            di[98] = 34;
            di[99] = 31;
            di[100] = 34;
            di[101] = 35;
            di[102] = 32;
            di[103] = 33;
            di[104] = 36;
            di[105] = 33;
            di[106] = 36;
            di[107] = 37;
        }
        Vector3 head, pos, pos0;
        sbyte uX = 1;
        sbyte uY = 1;
        sbyte pX = 1;
        sbyte pY = 1;
        sbyte p0X = 1;
        sbyte p0Y = 1;
        Sprite sp;
        Microsoft.DirectX.Direct3D.Font font0;
        System.Drawing.Font fonts = new System.Drawing.Font("Arial", 12, FontStyle.Bold);

        /*переменные для сдвига участков*/
        bool startshift = false;    //флаг начала сдвига
        bool finishshift = false;   //флаг окончания сдвига
        sbyte workX = 0;    //Х рабочего участка
        sbyte workY = 0;  //У рабочего участка
        MapList[,] workM = new MapList[4, 4];   //матрица участков для сдвига
        sbyte offsetX = 0;    //сдвиг по оси Х
        sbyte offsetY = 0;    //сдвиг по оси У
        List<int> lload = new List<int>();  //список загружаемых участков, используется при загрузке их из памяти и при переприсвоении
        sbyte startshiftX = 0;
        sbyte startshiftY = 0;
        sbyte finshiftX = 0;
        sbyte finshiftY = 0;
        sbyte stepX = 1;
        sbyte stepY = 1;
        //****************************************************************************************************************************

        private void shiftchunks(/*MapList[,] workM,int workX,int workY*/)   //подготовка регионов для смещения матрицы. сохранение удаляемых и загрузка/генерация новых
        {
            byte scx = 0;
            byte scy = 0;
            sbyte scz = 63;
            byte X = 0;
            byte Y = 0;
   
            
            while (1 == 1)
            {
                while (startshift == false) //пока загрузка карты не требуется, происходит обработка блоков
                {
                    m[X, Y].Map[scx, scy, scz] = checkblockloop(X, Y, scx, scy, (byte)scz, true);
                    m[uX, uY].Map[63-scx, 63 - scy, scz] = checkblockloop((byte)uX, (byte)uY, (byte)(63-scx), (byte)(63 - scy), (byte)scz, false);
                    m[uX, uY].Map[scx, scy, scz] = checkblockloop((byte)uX, (byte)uY, (byte)(scx), (byte)(scy), (byte)scz, false);
                    //Thread.Sleep(1);
                    scx++;
                    if (scx > 63)
                    {
                        scx = 0;
                        scy++;
                        if (scy > 63)
                        {
                            scy = 0;
                            scz--;
                            if (scz < 0)
                            {
                                
                                scz = 63;
                                X++;
                                if (X == 1 & Y == 1) X++;
                                if (X > 3)
                                {
                                    X = 0;
                                    Y++;
                                    if (Y > 3)
                                    {
                                        Y = 0;
                                    }
                                }
                            }
                        }
                    }
                 
                }

                List<int> lsave = new List<int>();
                lload.Clear();
                offsetX = 0;
                offsetY = 0;
                if (workX > 2)
                {
                    lsave.Add(0);
                    lsave.Add(1);
                    lsave.Add(2);
                    lsave.Add(3);
                    lload.Add(12);
                    lload.Add(13);
                    lload.Add(14);
                    lload.Add(15);
                    offsetX = 1;
                }
                else
                {
                    if (workX < 1)
                    {
                        lsave.Add(12);
                        lsave.Add(13);
                        lsave.Add(14);
                        lsave.Add(15);
                        lload.Add(0);
                        lload.Add(1);
                        lload.Add(2);
                        lload.Add(3);
                        offsetX = -1;
                    }
                }
                if (workY > 2)
                {
                    if (!lsave.Contains(0)) lsave.Add(0);
                    lsave.Add(4);
                    lsave.Add(8);
                    if (!lsave.Contains(12)) lsave.Add(12);

                    if (!lload.Contains(3)) lload.Add(3);
                    lload.Add(7);
                    lload.Add(11);
                    if (!lload.Contains(15)) lload.Add(15);
                    offsetY = 1;
                }
                else
                {
                    if (workY < 1)
                    {
                        if (!lsave.Contains(3)) lsave.Add(3);
                        lsave.Add(7);
                        lsave.Add(11);
                        if (!lsave.Contains(15)) lsave.Add(15);

                        if (!lload.Contains(0)) lload.Add(0);
                        lload.Add(4);
                        lload.Add(8);
                        if (!lload.Contains(12)) lload.Add(12);
                        offsetY = -1;
                    }
                }
                for (int ns = 0; ns < lsave.Count; ns++)
                {
                    savechunk(workM[lsave[ns] / 4, lsave[ns] % 4]);
                }

                //int sX = 0, fX = 0, sY = 0, fY = 0, pX = 0, pY = 0;
                if (offsetX == -1) { startshiftX = 3; finshiftX = -1; stepX = -1; }
                else { startshiftX = 0; finshiftX = 4; stepX = 1; }
                if (offsetY == -1) { startshiftY = 3; finshiftY = -1; stepY = -1; }
                else { startshiftY = 0; finshiftY = 4; stepY = 1; }
                for (int nx = startshiftX; nx != finshiftX; nx += stepX)
                {
                    for (int ny = startshiftY; ny != finshiftY; ny += stepY)
                    {
                        if (nx + offsetX >= 0 & nx + offsetX < 4 & ny + offsetY >= 0 & ny + offsetY < 4)
                        {
                            MapList mm = workM[nx + offsetX, ny + offsetY];
                            workM[nx, ny] = mm;
                        }
                    }
                }
                for (int ns = 0; ns < lload.Count; ns++)
                {
                    workM[lload[ns] / 4, lload[ns] % 4] = loadchunk((sbyte)(lload[ns] / 4), (sbyte)(lload[ns] % 4), (short)(m[lload[ns] / 4, lload[ns] % 4].X + offsetX), (short)(m[lload[ns] / 4, lload[ns] % 4].Y + offsetY));
                    workM[lload[ns] / 4, lload[ns] % 4] = checkvisiblechunk(workM, lload[ns] / 4, lload[ns] % 4);
                }
                finishshift = true;
                startshift = false;
            }
        }

        int test = 0;
        bvec2[] ool = new bvec2[16];
        private strmesh checkblock(byte pX, byte pY, byte x, byte y, byte z, bool trutach)
        {
            strmesh checkblock = m[pX, pY].Map[x, y, z];
            if (checkblock.type != 0)
            {
                if (checkblock.hill <= 0) //если НР блока = 0, уничтожаем
                {
                    drop[droped].type = checkblock.type;
                    drop[droped].col = 64;
                    drop[droped].time = 0;
                    drop[droped].position = new Vector3(pX * 512, pY * 512, 0) + new Vector3(x * 8 + 4, y * 8 + 4, z * 8 + 4);
                    drop[droped].acceleration = new Vector3(0, 0, 1);
                    droped++;
                    checkblock = new strmesh(false);

                    /*checkblock.hill = 0;
                    checkblock.sb = false;
                    checkblock.sbalt = new bool[4, 4, 4, 6];
                    checkblock.sbvis = new bool[4, 4, 4, 6];
                    checkblock.stype = new byte[4, 4, 4];
                    checkblock.svis = new bool[6];
                    checkblock.taches = new byte[10, 2];
                    checkblock.type = 0;
                    checkblock.bvis = false;
                    checkblock.alt = new bool[6];*/
                }
                else
                {
                    if (checkblock.hill > objectlist[checkblock.type].standartHill)
                    {

                    }
                }
            }
            bool ckvis = false;
            if (!checkblock.sb) //если проверяемый блок - целый
            {

                //checkblock.taches = new byte[3, 6];
                byte[] tc3 = new byte[6];
                for (byte s = 0; s < 6; s++)
                {
                    tc3[s] = checkblock.taches[2, s];
                }
                checkblock.taches = new byte[3, 6];
                for (byte s = 0; s < 6; s++)
                {
                    checkblock.taches[2, s] = tc3[s];
                }
                for (byte vis = 0; vis < 6; vis++)
                {
                    if (checkblock.type == 7) { if (!checkblock.alt[vis]) checkblock.taches[0, vis] = 15; }
                    byte workX = pX;
                    byte workY = pY;
                    sbyte workx = (sbyte)(x + shifting[vis].X);
                    if (workx > 63) { if (workX < 3) { workx -= 64; workX++; } else workx = 63; } else if (workx < 0) { if (workX > 0) { workx += 64; workX--; } else workx = 0; }
                    sbyte worky = (sbyte)(y + (int)shifting[vis].Y);
                    if (worky > 63) { if (workY < 3) { worky -= 64; workY++; } else worky = 63; } else if (worky < 0) { if (workY > 0) { worky += 64; workY--; } else worky = 0; }
                    sbyte workz = (sbyte)(z + (int)shifting[vis].Z);
                    if (workz >= 64) { workz = 63; } else if (workz < 0) { workz = 0; }
                    strmesh workblock = m[workX, workY].Map[workx, worky, workz];

                    if (!workblock.sb)  //если с боку полный блок
                    {
                        checkblock.vis[vis] = (workblock.type != 0 | checkblock.type == 0) ? false : true; //и не пустой  //сторона проверяемого блока не видима  
                        if (workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2] > 0)
                        {
                            if (checkblock.type == 0)
                            {
                                for (byte sh = 0; sh < 6; sh++)
                                {
                                    if (vis == 0 & workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2] == 20) { checkblock.taches[0, sh] = (byte)(workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2]); }
                                    else if (workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2] - 2 > checkblock.taches[0, sh]) checkblock.taches[0, sh] = (byte)(workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2] - 2);
                                }
                            }
                            else
                            {
                                checkblock.taches[1, vis] = workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2];
                            }
                        }
                    }
                    else//если с боку неполный блок
                    {
                        checkblock.vis[vis] = true;
                        sbyte wsx = 0;
                        sbyte wfx = 0;
                        sbyte wsy = 0;
                        sbyte wfy = 0;
                        sbyte wsz = 0;
                        sbyte wfz = 0;
                        if (shifting[vis].X == 0)
                        {
                            wsx = 0;
                            wfx = 3;
                        }
                        else
                        {
                            wsx = (sbyte)((shifting[vis].X + 4) % 5);
                            wfx = wsx;
                        }
                        if (shifting[vis].Y == 0)
                        {
                            wsy = 0;
                            wfy = 3;
                        }
                        else
                        {
                            wsy = (sbyte)((shifting[vis].Y + 4) % 5);
                            wfy = wsy;
                        }
                        if (shifting[vis].Z == 0)
                        {
                            wsz = 0;
                            wfz = 3;
                        }
                        else
                        {
                            wsz = (sbyte)((shifting[vis].Z + 4) % 5);
                            wfz = wsz;
                        }
                        for (sbyte sx = wsx; sx <= wfx; sx++)
                        {
                            for (sbyte sy = wsy; sy <= wfy; sy++)
                            {
                                for (sbyte sz = wsz; sz <= wfz; sz++)
                                {
                                    if (checkblock.type == 0)
                                    {
                                        for (byte sh = 0; sh < 6; sh++)
                                        {
                                            if (vis == 0 & workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0] == 20) checkblock.taches[0, sh] = (byte)(workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0]);
                                            else if (workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0] - 2 > checkblock.taches[0, sh]) checkblock.taches[0, sh] = (byte)(workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0] - 2);
                                        }
                                    }
                                    else if (checkblock.taches[1, vis] < workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0]) { checkblock.taches[1, vis] = workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0]; }
                                }
                            }
                        }
                    }
                    //  if (workM[workX, workY].Map[workx, worky, workz].taches[0, (vis / 2) * 2 + (vis +1) % 2] > 0)
                    // {
                    if (checkblock.vis[vis] == true) ckvis = true;
                    if (trutach)
                    {
                        if (checkblock.type == 5)
                        {
                            //if(x==7 & y==7 & z==23){}
                            if (checkblock.taches[1, vis] > 8 & sune > 200 & rand.Next(992) + (checkblock.taches[1, vis] - 8) * 2 > 990)
                                checkblock.alt[vis] = true;
                            if (checkblock.taches[1, vis] < 5 & rand.Next(1000) > 990)
                                checkblock.alt[vis] = false;
                        }
                    }
                }
            }
            else
            {
                
                byte typeb = checkblock.sbtype[0, 0, 0];
                for (byte sx = 0; sx < 4; sx++)
                {
                    for (byte sy = 0; sy < 4; sy++)
                    {
                        for (sbyte sz = 3; sz >= 0; sz--)
                        {
                            for (byte svis = 0; svis < 6; svis++)
                            {
                                checkblock.sbtach[sx, sy, sz, svis, 0] = 0;
                                checkblock.sbtach[sx, sy, sz, svis, 1] = 0;
                            }
                            
                            for (byte svis = 0; svis < 6; svis++)
                            {

                                checkblock.sbvis[sx, sy, sz, svis] = true;
                                //checkblock.sbtach[sx, sy, sz, svis, 1] = 10;
                                byte workX = pX;
                                byte workY = pY;
                                sbyte workx = (sbyte)x;
                                sbyte worky = (sbyte)y;
                                sbyte workz = (sbyte)z;
                                sbyte wsx = (sbyte)(sx + shifting[svis].X);
                                sbyte wsy = (sbyte)(sy + shifting[svis].Y);
                                sbyte wsz = (sbyte)(sz + shifting[svis].Z);
                                if (wsx < 0 | wsx > 3)
                                {
                                    wsx = (sbyte)((wsx + 4) % 4);//!!!!!!!!!!!!!!!!!!!!!!!
                                    workx = (sbyte)(x + shifting[svis].X);
                                    if (workx > 63) { if (workX < 3) { workx -= 64; workX++; } else workx = 63; } else if (workx < 0) { if (workX > 0) { workx += 64; workX--; } else workx = 0; }
                                }
                                if (wsy < 0 | wsy > 3)
                                {
                                    wsy = (sbyte)((wsy + 4) % 4);
                                    worky = (sbyte)(y + (int)shifting[svis].Y);
                                    if (worky > 63) { if (workY < 3) { worky -= 64; workY++; } else worky = 63; } else if (worky < 0) { if (workY > 0) { worky += 64; workY--; } else worky = 0; }
                                }
                                if (wsz < 0 | wsz > 3)
                                {
                                    wsz = (sbyte)((wsz + 4) % 4);
                                    workz = (sbyte)(z + (int)shifting[svis].Z);
                                    if (workz >= 64) { workz = 63; } else if (workz < 0) { workz = 0; }
                                }
                                strmesh workblock = m[workX, workY].Map[workx, worky, workz];

                                if (checkblock.sbtype[sx, sy, sz] == 0)
                                {
                                    //if (svis != 1)
                                    {
                                        for (byte sh = 0; sh < 6; sh++)
                                        {
                                            if (workblock.sb)
                                            {
                                                if (svis == 0 & workblock.sbtach[sx, sy, (sz + 1) % 4, (svis / 2) * 2 + (svis + 1) % 2, 0] == 20) { checkblock.sbtach[sx, sy, sz, sh, 0] = (byte)(workblock.sbtach[sx, sy, (sz + 1) % 4, (svis / 2) * 2 + (svis + 1) % 2, 0]); }
                                                else if (workblock.sbtach[wsx, wsy, wsz, (svis / 2) * 2 + (svis + 1) % 2, 0] - 2 > checkblock.sbtach[sx, sy, sz, sh, 0]) checkblock.sbtach[sx, sy, sz, sh, 0] = (byte)(workblock.sbtach[wsx, wsy, wsz, (svis / 2) * 2 + (svis + 1) % 2, 0] - 2);
                                                //if (checkblock.sbtach[sx, sy, sz, sh, 0] > 20) checkblock.sbtach[sx, sy, sz, sh, 0] = 20;
                                                //  checkblock.sbtach[sx, sy, sz, svis, 0] = (byte)(workblock.sbtach[wsx, wsy, wsz, (svis / 2) * 2 + (svis + 1) % 2, 1] * 4);
                                            }
                                            else
                                            {
                                                if (svis == 0 & workblock.taches[0, (svis / 2) * 2 + (svis + 1) % 2] == 20) { checkblock.sbtach[sx, sy, sz, sh, 0] = (byte)(workblock.taches[0, (svis / 2) * 2 + (svis + 1) % 2]); }
                                                else if ((workblock.taches[0, (svis / 2) * 2 + (svis + 1) % 2] - 2) > checkblock.sbtach[sx, sy, sz, sh, 0]) checkblock.sbtach[sx, sy, sz, sh, 0] = (byte)(workblock.taches[0, (svis / 2) * 2 + (svis + 1) % 2] - 2);
                                                //if (checkblock.sbtach[sx, sy, sz, sh, 0] > 20) checkblock.sbtach[sx, sy, sz, sh, 0] = 20;
                                                //checkblock.sbtach[sx, sy, sz, sh, 0] = (byte)(workblock.taches[(svis / 2) * 2 + (svis + 1) % 2, 0]/4);
                                            }
                                        }
                                    }
                                }

                                else
                                {
                                    if (workblock.sb)
                                    {
                                        checkblock.sbtach[sx, sy, sz, svis, 1] = (byte)(workblock.sbtach[wsx, wsy, wsz, (svis / 2) * 2 + (svis + 1) % 2, 0] /* * 4*/);
                                    }
                                    else
                                    {
                                        checkblock.sbtach[sx, sy, sz, svis, 1] = (byte)(workblock.taches[0, (svis / 2) * 2 + (svis + 1) % 2]);
                                    }
                                }
                                if (checkblock.sbvis[sx, sy, sz, svis] == true) ckvis = true;
                                if (trutach)
                                {
                                    if (checkblock.sbtype[sx, sy, sz] == 5)
                                    {
                                        if (checkblock.sbtach[sx, sy, sz, svis, 1] > 8 & sune > 200 & rand.Next(992) + (checkblock.sbtach[sx, sy, sz, svis, 1] - 8) * 5 > 990)
                                            checkblock.sbalt[sx, sy, sz, svis] = true;
                                        if (checkblock.sbtach[sx, sy, sz, svis, 1] < 5 & rand.Next(1000) > 990)
                                            checkblock.sbalt[sx, sy, sz, svis] = false;
                                    }
                                }
                            }
                        }
                    }
                }
                
            }
            checkblock.bvis = (ckvis & checkblock.type != 0) ? true : false;
            return checkblock;
        }

        private strmesh checkblockloop(byte pX, byte pY, byte cbx, byte cby, byte sbz, bool trutach)
        {
            strmesh checkblock = m[pX, pY].Map[cbx, cby, sbz];
            if (checkblock.type != 0)
            {
                if (checkblock.hill <= 0) //если НР блока = 0, уничтожаем
                {
                    drop[droped].type = checkblock.type;
                    drop[droped].col = 64;
                    drop[droped].time = 0;
                    drop[droped].position = new Vector3(pX * 512, pY * 512, 0) + new Vector3(cbx * 8 + 4, cby * 8 + 4, sbz * 8 + 4);
                    drop[droped].acceleration = new Vector3(0, 0, 1);
                    droped++;
                    checkblock = new strmesh(false);

                    /*checkblock.hill = 0;
                    checkblock.sb = false;
                    checkblock.sbalt = new bool[4, 4, 4, 6];
                    checkblock.sbvis = new bool[4, 4, 4, 6];
                    checkblock.stype = new byte[4, 4, 4];
                    checkblock.svis = new bool[6];
                    checkblock.taches = new byte[10, 2];
                    checkblock.type = 0;
                    checkblock.bvis = false;
                    checkblock.alt = new bool[6];*/
                }
                else
                {
                    if (checkblock.hill > objectlist[checkblock.type].standartHill)
                    {

                    }
                }
            }
            bool ckvis = false;
            if (!checkblock.sb) //если проверяемый блок - целый
            {
                /* if (objectlist[checkblock.type].type == "liguid block" & pX==uX & pY==uY & trutach)
                 {
                     //label1.Text += " x=" + x.ToString()+" y=" + y.ToString()+" z=" + z.ToString() + " " + checkblock.taches[2, 1];
                 }*/
                byte[] tc3 = new byte[6];
                for (byte s = 0; s < 6; s++)
                {
                    tc3[s] = checkblock.taches[2, s];
                }
                checkblock.taches = new byte[3, 6];
                for (byte s = 0; s < 6; s++)
                {
                    checkblock.taches[2, s] = tc3[s];
                }
                /*for (byte s = 0; s < 6;s++ )
                {
                    checkblock.taches[0, s] = new byte();
                    checkblock.taches[1, s] = new byte();
                    checkblock.taches[2, s] = new byte();
                }*/
                //byte colvis = 0;
                for (byte vis = 0; vis < 6; vis++)
                {
                    if (checkblock.type == 7) { if (!checkblock.alt[vis]) checkblock.taches[0, vis] = 15; }
                    byte workX = pX;
                    byte workY = pY;
                    sbyte workx = (sbyte)(cbx + shifting[vis].X);
                    if (workx > 63) { if (workX < 3) { workx -= 64; workX++; } else workx = 63; } else if (workx < 0) { if (workX > 0) { workx += 64; workX--; } else workx = 0; }
                    sbyte worky = (sbyte)(cby + (int)shifting[vis].Y);
                    if (worky > 63) { if (workY < 3) { worky -= 64; workY++; } else worky = 63; } else if (worky < 0) { if (workY > 0) { worky += 64; workY--; } else worky = 0; }
                    sbyte workz = (sbyte)(sbz + (int)shifting[vis].Z);
                    if (workz >= 64) { workz = 63; } else if (workz < 0) { workz = 0; }
                    strmesh workblock = m[workX, workY].Map[workx, worky, workz];
                ckbigwksm: if (!workblock.sb)  //если с боку полный блок
                    {
                        checkblock.vis[vis] = (workblock.type != 0 | checkblock.type == 0) ? false : true; //и не пустой  //сторона проверяемого блока не видима
                        if (objectlist[checkblock.type].type == "liguid block")
                        {
                            if (objectlist[workblock.type].type == "Gas block" & checkblock.taches[2, vis] > 0)
                            {
                                workblock = new strmesh(true);
                                workblock.type = checkblock.type;
                                workblock.bvis = true;

                                for (byte cbSb_X = 0; cbSb_X < 3; cbSb_X++)
                                {
                                    for (byte cbSb_Y = 0; cbSb_Y < 3; cbSb_Y++)
                                    {
                                        for (byte cbSb_Z = 0; cbSb_Z < 3; cbSb_Z++)
                                        {
                                            for (byte cbSCH = 0; cbSCH < 6; cbSCH++)
                                            {
                                                workblock.sbvis[cbSb_X, cbSb_Y, cbSb_Z, cbSCH] = true;
                                                workblock.sbtach[cbSb_X, cbSb_Y, cbSb_Z, cbSCH, 0] = m[workX, workY].Map[workx, worky, workz].taches[0, cbSCH];
                                            }
                                        }
                                    }
                                }
                                //m[workX, workY].Map[workx, worky, workz].hill = objectlist[checkblock.type].standartHill;
                                m[workX, workY].Map[workx, worky, workz] = workblock;
                                goto ckbigwksm;
                            }
                            else
                            {
                                if (!workblock.sb & objectlist[workblock.type].type == "liguid block")
                                {
                                    for (byte sh = 1; sh < 6; sh++)
                                    {
                                        if ((workblock.taches[2, sh] < checkblock.taches[2, sh] - 4 & vis != 1) | (workblock.taches[2, sh] < checkblock.taches[2, sh] + 4 & vis == 1))
                                        {
                                            m[workX, workY].Map[workx, worky, workz].taches[2, sh] = (vis == 1) ? (byte)(checkblock.taches[2, sh] + 4) : (byte)(checkblock.taches[2, sh] - 4);
                                        }
                                    }
                                    if (vis == 0 & workblock.taches[2, 0] < checkblock.taches[2, 0] - 4)
                                    {
                                        m[workX, workY].Map[workx, worky, workz].taches[2, 0] = (byte)(checkblock.taches[2, 0] - 4);
                                    }
                                }
                                /*if(checkblock.taches[2, 1] - 4 > 0)
                                {
                                    if (workblock.taches[2, 1] < checkblock.taches[2, 1] - 5 | (workblock.taches[2, 1] < checkblock.taches[2, 1] + 4 & vis == 1))
                                        for (byte sh = 1; sh < 6; sh++)
                                            m[workX, workY].Map[workx, worky, workz].taches[2, sh] = (vis == 1) ? (byte)(checkblock.taches[2, sh] + 4) : (byte)(checkblock.taches[2, sh] - 4);
                                    if (vis == 0 & workblock.taches[2, 0] < checkblock.taches[2, vis] - 4)
                                        m[workX, workY].Map[workx, worky, workz].taches[2, 0] = (byte)(checkblock.taches[2, vis] - 4);
                                }*/
                            }
                            /*   if (!checkblock.vis[1]) { if (vis > 1 & !checkblock.alt[1]) checkblock.alt[vis] = checkblock.vis[vis]; }
                               else { checkblock.alt[1] = true; }*/
                        }
                        if (workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2] > 0)
                        {
                            if (checkblock.type == 0)
                            {
                                for (byte sh = 0; sh < 6; sh++)
                                {
                                    if (vis == 0 & workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2] == 20) { checkblock.taches[0, sh] = (byte)(workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2]); }
                                    else if (workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2] - 2 > checkblock.taches[0, sh]) checkblock.taches[0, sh] = (byte)(workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2] - 2);
                                }
                            }
                            else
                            {
                                checkblock.taches[1, vis] = workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2];
                            }
                        }
                        /*   if (checkblock.type == 0 & objectlist[workblock.type].type == "liguid block" & vis != 1)
                           {
                               checkblock = new strmesh(false);
                               checkblock.type = workblock.type;
                               checkblock.bvis = true;
                               checkblock.hill = objectlist[workblock.type].standartHill;
                           }*/
                    }
                    else//если с боку неполный блок
                    {
                        checkblock.vis[vis] = true;

                        sbyte wsx = 0;
                        sbyte wfx = 0;
                        sbyte wsy = 0;
                        sbyte wfy = 0;
                        sbyte wsz = 0;
                        sbyte wfz = 0;
                        if (shifting[vis].X == 0)
                        {
                            wsx = 0;
                            wfx = 3;
                        }
                        else
                        {
                            wsx = (sbyte)((shifting[vis].X + 4) % 5);
                            wfx = wsx;
                        }
                        if (shifting[vis].Y == 0)
                        {
                            wsy = 0;
                            wfy = 3;
                        }
                        else
                        {
                            wsy = (sbyte)((shifting[vis].Y + 4) % 5);
                            wfy = wsy;
                        }
                        if (shifting[vis].Z == 0)
                        {
                            wsz = 0;
                            wfz = 3;
                        }
                        else
                        {
                            wsz = (sbyte)((shifting[vis].Z + 4) % 5);
                            wfz = wsz;
                        }
                        for (sbyte sx = wsx; sx <= wfx; sx++)
                        {
                            for (sbyte sy = wsy; sy <= wfy; sy++)
                            {
                                for (sbyte sz = wfz; sz >= wsz; sz--)
                                {


                                    if (objectlist[checkblock.type].type == "Gas block")
                                    {
                                        for (byte sh = 0; sh < 6; sh++)
                                        {
                                            if (vis == 0 & workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0] == 20) checkblock.taches[0, sh] = (byte)(workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0]);
                                            else if (workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0] - 2 > checkblock.taches[0, sh]) checkblock.taches[0, sh] = (byte)(workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0] - 2);
                                        }
                                    }
                                    else
                                    {
                                        if (checkblock.taches[1, vis] < workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0])
                                        { checkblock.taches[1, vis] = workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0]; }
                                        if (objectlist[checkblock.type].type == "liguid block")
                                        {

                                            sbyte workz2 = workz;
                                            sbyte z2wb = (sbyte)(sz - 1);
                                            if (z2wb < 0)
                                            {
                                                workz2--;
                                                z2wb += 4;
                                            }
                                            if (workz2 < 0)
                                            {
                                                workz2 = workz;
                                                z2wb = sz;
                                            }
                                            strmesh workblock2 = m[workX, workY].Map[workx, worky, workz2]; //на один маленький блок ниже рабочего
                                            //strmesh workblock3 = m[workX, workY].Map[workx + shifting[vis].X, worky + shifting[vis].Y, workz2]; //на один маленький блок ниже рабочего и на один вперёд
                                            if (objectlist[workblock.sbtype[sx, sy, sz]].type == "Gas block")
                                            {
                                                if (workblock2.sb)
                                                {
                                                    if (((vis > 1 & ((objectlist[workblock2.sbtype[sx, sy, z2wb]].type != "Gas block" & objectlist[workblock2.sbtype[sx + shifting[vis].X, sy + shifting[vis].Y, z2wb]].type != "Gas block") | sz == 0)) | vis <= 1) & checkblock.taches[2, vis] > 0)
                                                    {
                                                        //m[workX, workY].Map[workx, worky, workz] = new strmesh(false);
                                                        m[workX, workY].Map[workx, worky, workz].sbtype[sx, sy, sz] = checkblock.type;
                                                        for (byte sh = 0; sh < 6; sh++)
                                                        {
                                                            m[workX, workY].Map[workx, worky, workz].sbtach[sx, sy, sz, sh, 0] = 0;
                                                            m[workX, workY].Map[workx, worky, workz].sbtach[sx, sy, sz, sh, 1] = checkblock.taches[1, vis];
                                                            m[workX, workY].Map[workx, worky, workz].sbvis[sx, sy, sz, sh] = true;
                                                            //if(sh!=0) m[workX, workY].Map[workx, worky, workz].sbtach[sx, sy, sz, sh, 2] = (byte)(checkblock.taches[2, vis] - 1);
                                                        }
                                                        //m[workX, workY].Map[workx, worky, workz].hill += (short)(objectlist[checkblock.type].standartHill/64);
                                                        workblock = m[workX, workY].Map[workx, worky, workz];
                                                    }
                                                }
                                                else
                                                {
                                                    if (((vis > 1 & (objectlist[workblock2.type].type != "Gas block" | sz == 0)) | (vis <= 1)) & checkblock.taches[2, vis] > 0)
                                                    {
                                                        //m[workX, workY].Map[workx, worky, workz] = new strmesh(false);
                                                        m[workX, workY].Map[workx, worky, workz].sbtype[sx, sy, sz] = checkblock.type;
                                                        for (byte sh = 0; sh < 6; sh++)
                                                        {
                                                            m[workX, workY].Map[workx, worky, workz].sbtach[sx, sy, sz, sh, 0] = 0;
                                                            m[workX, workY].Map[workx, worky, workz].sbtach[sx, sy, sz, sh, 1] = checkblock.taches[1, vis];
                                                            m[workX, workY].Map[workx, worky, workz].sbvis[sx, sy, sz, sh] = true;
                                                            //if(sh!=0) m[workX, workY].Map[workx, worky, workz].sbtach[sx, sy, sz, sh, 2] = (byte)(checkblock.taches[2, vis] - 1);
                                                        }
                                                        //m[workX, workY].Map[workx, worky, workz].hill += (short)(objectlist[checkblock.type].standartHill/64);
                                                        workblock = m[workX, workY].Map[workx, worky, workz];
                                                    }
                                                }
                                            }
                                            else
                                            {

                                                if (objectlist[workblock.sbtype[sx, sy, sz]].type == "liguid block")
                                                {
                                                    for (byte sh = 1; sh < 6; sh++)
                                                    {
                                                        if ((workblock.sbtach[sx, sy, sz, sh, 2] < checkblock.taches[2, sh] - 1 & vis != 1) | (workblock.sbtach[sx, sy, sz, sh, 2] < checkblock.taches[2, sh] + 1 & vis == 1))
                                                            m[workX, workY].Map[workx, worky, workz].sbtach[sx, sy, sz, sh, 2] = (vis == 1) ? (byte)(checkblock.taches[2, sh] + 1) : (byte)(checkblock.taches[2, sh] - 1);
                                                    }
                                                    if (vis == 0 & workblock.sbtach[sx, sy, sz, 0, 2] < checkblock.taches[2, 0] - 1)
                                                    {
                                                        m[workX, workY].Map[workx, worky, workz].sbtach[sx, sy, sz, 0, 2] = (byte)(checkblock.taches[2, 0] - 1);
                                                    }
                                                    //workblock = m[workX, workY].Map[workx, worky, workz];
                                                }
                                            }
                                            /*if (!checkblock.vis[1]) { if (vis > 1 & !checkblock.alt[1]) checkblock.alt[vis] = checkblock.vis[vis]; }
                                            else { checkblock.alt[1] = true; }*/

                                        }
                                        
                                    }
                                }
                            }
                        }
                    }
                    //  if (workM[workX, workY].Map[workx, worky, workz].taches[0, (vis / 2) * 2 + (vis +1) % 2] > 0)
                    // {
                    if (checkblock.vis[vis] == true) ckvis = true;
                    if (trutach)
                    {
                        if (checkblock.type == 5)
                        {
                            //if(x==7 & y==7 & z==23){}
                            if (checkblock.taches[1, vis] > 8 & sune > 200 & rand.Next(992) + (checkblock.taches[1, vis] - 8) * 2 > 990)
                                checkblock.alt[vis] = true;
                            if (checkblock.taches[1, vis] < 5 & rand.Next(1000) > 990)
                                checkblock.alt[vis] = false;
                        }
                    }

                    // if (checkblock.vis[vis]) colvis++;
                }
                //  if (objectlist[checkblock.type].type == "liguid block"/* & colvis > 0 &  checkblock.hill>objectlist[checkblock.type].standartHill+10*/)
                /*    {
                        //byte waterset = 0;
                        for (sbyte vis = 1; vis <=5 ; vis++)
                        {
                            if (checkblock.vis[vis] & checkblock.taches[2, vis]>0)
                            {

                                //checkblock.taches[2, vis] = (byte)((checkblock.hill - objectlist[checkblock.type].standartHill) / colvis);
                                //checkblock.hill -= (byte)((checkblock.hill - objectlist[checkblock.type].standartHill) / colvis);
                            }
                        }
                    }*/

            }
            else
            {
                bool ck = false;
                byte typeb = checkblock.sbtype[0, 0, 0];
                for (byte se = 0; se < 16; se++)
                {
                    byte sx=(byte)ool[se].X;
                    byte sy=(byte)ool[se].Y;
                        for (sbyte sz = 3; sz >= 0; sz--)
                        {

                            for (byte svis = 0; svis < 6; svis++)
                            {
                                checkblock.sbtach[sx, sy, sz, svis, 0] = 0;
                                checkblock.sbtach[sx, sy, sz, svis, 1] = 0;
                            }
                            if (typeb != checkblock.sbtype[sx, sy, sz]) ck = true;
                            for (byte svis = 0; svis < 6; svis++)
                            {

                                checkblock.sbvis[sx, sy, sz, svis] = true;
                                //checkblock.sbtach[sx, sy, sz, svis, 1] = 10;
                                byte workX = pX;
                                byte workY = pY;
                                sbyte workx = (sbyte)cbx;
                                sbyte worky = (sbyte)cby;
                                sbyte workz = (sbyte)sbz;
                                sbyte wsx = (sbyte)(sx + shifting[svis].X);
                                sbyte wsy = (sbyte)(sy + shifting[svis].Y);
                                sbyte wsz = (sbyte)(sz + shifting[svis].Z);
                                if (wsx < 0 | wsx > 3)
                                {
                                    wsx = (sbyte)((wsx + 4) % 4);//!!!!!!!!!!!!!!!!!!!!!!!
                                    workx = (sbyte)(cbx + shifting[svis].X);
                                    if (workx > 63) { if (workX < 3) { workx -= 64; workX++; } else workx = 63; } else if (workx < 0) { if (workX > 0) { workx += 64; workX--; } else workx = 0; }
                                }
                                if (wsy < 0 | wsy > 3)
                                {
                                    wsy = (sbyte)((wsy + 4) % 4);
                                    worky = (sbyte)(cby + (int)shifting[svis].Y);
                                    if (worky > 63) { if (workY < 3) { worky -= 64; workY++; } else worky = 63; } else if (worky < 0) { if (workY > 0) { worky += 64; workY--; } else worky = 0; }
                                }
                                if (wsz < 0 | wsz > 3)
                                {
                                    wsz = (sbyte)((wsz + 4) % 4);
                                    workz = (sbyte)(sbz + (int)shifting[svis].Z);
                                    if (workz >= 64) { workz = 63; } else if (workz < 0) { workz = 0; }
                                }
                                strmesh workblock = m[workX, workY].Map[workx, worky, workz];

                                if (checkblock.sbtype[sx, sy, sz] == 0)
                                {
                                    checkblock.sbvis[sx, sy, sz, svis] = false;
                                    for (byte sh = 0; sh < 6; sh++)
                                    {
                                        if (workblock.sb)
                                        {
                                            if (svis == 0 & workblock.sbtach[sx, sy, (sz + 1) % 4, (svis / 2) * 2 + (svis + 1) % 2, 0] == 20) { checkblock.sbtach[sx, sy, sz, sh, 0] = (byte)(workblock.sbtach[sx, sy, (sz + 1) % 4, (svis / 2) * 2 + (svis + 1) % 2, 0]); }
                                            else if (workblock.sbtach[wsx, wsy, wsz, (svis / 2) * 2 + (svis + 1) % 2, 0] - 2 > checkblock.sbtach[sx, sy, sz, sh, 0]) checkblock.sbtach[sx, sy, sz, sh, 0] = (byte)(workblock.sbtach[wsx, wsy, wsz, (svis / 2) * 2 + (svis + 1) % 2, 0] - 2);
                                        }
                                        else
                                        {
                                            if (svis == 0 & workblock.taches[0, (svis / 2) * 2 + (svis + 1) % 2] == 20) { checkblock.sbtach[sx, sy, sz, sh, 0] = (byte)(workblock.taches[0, (svis / 2) * 2 + (svis + 1) % 2]); }
                                            else if ((workblock.taches[0, (svis / 2) * 2 + (svis + 1) % 2] - 2) > checkblock.sbtach[sx, sy, sz, sh, 0]) checkblock.sbtach[sx, sy, sz, sh, 0] = (byte)(workblock.taches[0, (svis / 2) * 2 + (svis + 1) % 2] - 2);
                                            //if (checkblock.sbtach[sx, sy, sz, sh, 0] > 20) checkblock.sbtach[sx, sy, sz, sh, 0] = 20;
                                            //checkblock.sbtach[sx, sy, sz, sh, 0] = (byte)(workblock.taches[(svis / 2) * 2 + (svis + 1) % 2, 0]/4);
                                        }
                                    }
                                }
                                else
                                {

                                cksmwksm: if (workblock.sb)
                                    {
                                        if (objectlist[workblock.sbtype[wsx, wsy, wsz]].type == "Gas block") checkblock.sbvis[sx, sy, sz, svis] = true; else checkblock.sbvis[sx, sy, sz, svis] = false;
                                        checkblock.sbtach[sx, sy, sz, svis, 1] = (byte)(workblock.sbtach[wsx, wsy, wsz, (svis / 2) * 2 + (svis + 1) % 2, 0] /* * 4*/);
                                        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                                        if (objectlist[checkblock.sbtype[sx, sy, sz]].type == "liguid block")
                                        {
                                            if (objectlist[workblock.sbtype[wsx, wsy, wsz]].type == "Gas block")
                                            {
                                                sbyte wsz2 = (sbyte)(wsz - 1);
                                                sbyte workz2 = workz;

                                                if (wsz2 < 0)
                                                {
                                                    wsz2 = (sbyte)((wsz2 + 4) % 4);
                                                    workz2--;
                                                    if (workz2 < 0)
                                                    {
                                                        wsz2 = wsz;
                                                        workz2 = workz;
                                                    }
                                                }
                                                strmesh workblock2 = m[workX, workY].Map[workx, worky, workz2];
                                                sbyte workx2 = workx;
                                                sbyte worky2 = worky;
                                                sbyte workX2 = (sbyte)workX;
                                                sbyte workY2 = (sbyte)workY;
                                                sbyte wsx2 = (sbyte)(wsx + shifting[svis].X);
                                                sbyte wsy2 = (sbyte)(wsy + shifting[svis].Y);
                                                if (wsx2 < 0 | wsx2 > 3)
                                                {
                                                    wsx2 = (sbyte)((wsx2 + 4) % 4);
                                                    workx2 += shifting[svis].X;
                                                    if (workx2 < 0 | workx2 > 63)
                                                    {
                                                        workx2 = (sbyte)((workx2 + 64) % 64);
                                                        workX2 += shifting[svis].X;
                                                        if (workX2 < 0 | workX2 > 3)
                                                        {
                                                            wsx2 = wsx;
                                                            workx2 = workx;
                                                            workX2 = (sbyte)workX;
                                                        }
                                                    }
                                                }

                                                if (wsy2 < 0 | wsy2 > 3)
                                                {
                                                    wsy2 = (sbyte)((wsy2 + 4) % 4);
                                                    worky2 += shifting[svis].Y;
                                                    if (worky2 < 0 | worky2 > 63)
                                                    {
                                                        worky2 = (sbyte)((worky2 + 64) % 64);
                                                        workY2 += shifting[svis].Y;
                                                        if (workY2 < 0 | workY2 > 3)
                                                        {
                                                            wsy2 = wsy;
                                                            worky2 = worky;
                                                            workY2 = (sbyte)workY;
                                                        }
                                                    }
                                                }
                                                strmesh workblock3 = m[workX2, workY2].Map[workx2, worky2, workz2];
                                                bool tryy = false;
                                                if (svis > 1)
                                                {
                                                    if (workblock2.sb)
                                                    {
                                                        if (objectlist[workblock2.sbtype[wsx, wsy, wsz2]].type != "Gas block")
                                                        {
                                                            if (workblock3.sb)
                                                            {
                                                                if (objectlist[workblock3.sbtype[wsx2, wsy2, wsz2]].type != "Gas block")
                                                                    tryy = true;
                                                            }
                                                            else
                                                            {
                                                                if (objectlist[workblock3.type].type != "Gas block")
                                                                    tryy = true;
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {
                                                        if (objectlist[workblock2.type].type != "Gas block")
                                                        {
                                                            if (workblock3.sb)
                                                            {
                                                                if (objectlist[workblock3.sbtype[wsx2, wsy2, wsz2]].type != "Gas block")
                                                                    tryy = true;
                                                            }
                                                            else
                                                            {
                                                                if (objectlist[workblock3.type].type != "Gas block")
                                                                    tryy = true;
                                                            }
                                                        }
                                                    }

                                                    if (sz == 0)
                                                    {
                                                        if (workx == cbx & worky == cby & workX == pX & workY == pY)
                                                            tryy = true;
                                                    }
                                                }
                                                else tryy = true;

                                                if (checkblock.sbtach[sx, sy, sz, svis, 2] > 0 & tryy)
                                                {
                                                    m[workX, workY].Map[workx, worky, workz].sbtype[wsx, wsy, wsz] = checkblock.sbtype[sx, sy, sz];
                                                    for (byte sh = 0; sh < 6; sh++)
                                                    {
                                                        m[workX, workY].Map[workx, worky, workz].sbtach[wsx, wsy, wsz, sh, 0] = 0;
                                                        m[workX, workY].Map[workx, worky, workz].sbtach[wsx, wsy, wsz, sh, 1] = checkblock.sbtach[sx, sy, sz, svis, 1];
                                                        m[workX, workY].Map[workx, worky, workz].sbvis[wsx, wsy, wsz, sh] = true;
                                                        //if (sh!=0) m[workX, workY].Map[workx, worky, workz].sbtach[wsx, wsy, wsz, sh, 2] = (byte)(checkblock.sbtach[sx, sy, sz,svis,2]-1); 
                                                    }
                                                    /*  m[workX, workY].Map[workx, worky, workz].hill += (short)(objectlist[checkblock.type].standartHill / 64);*/
                                                    workblock = m[workX, workY].Map[workx, worky, workz];
                                                }
                                            }
                                            else
                                            {
                                                if (objectlist[workblock.sbtype[wsx, wsy, wsz]].type == "liguid block")
                                                {
                                                    for (byte sh = 1; sh < 6; sh++)
                                                    {
                                                        if ((workblock.sbtach[wsx, wsy, wsz, sh, 2] < checkblock.sbtach[sx, sy, sz, sh, 2] - 1 & svis != 1) | (workblock.sbtach[wsx, wsy, wsz, sh, 2] < checkblock.sbtach[sx, sy, sz, sh, 2] + 1) & svis == 1)
                                                            m[workX, workY].Map[workx, worky, workz].sbtach[wsx, wsy, wsz, sh, 2] = (svis == 1) ? (byte)(checkblock.sbtach[sx, sy, sz, sh, 2] + 1) : (byte)(checkblock.sbtach[sx, sy, sz, sh, 2] - 1);
                                                    }
                                                    if (svis == 0 & workblock.sbtach[wsx, wsy, wsz, 0, 2] < checkblock.sbtach[sx, sy, sz, 0, 2] - 1)
                                                    {
                                                        m[workX, workY].Map[workx, worky, workz].sbtach[wsx, wsy, wsz, 0, 2] = (byte)(checkblock.sbtach[sx, sy, sz, 0, 2] - 1);
                                                    }
                                                }
                                            }

                                            /*    if (!checkblock.sbvis[sx, sy, sz, 1]) { if (svis > 1) { checkblock.sbalt[sx, sy, sz, 1] = false; checkblock.sbalt[sx, sy, sz, svis] = checkblock.sbvis[sx, sy, sz, svis]; } }
                                                else { checkblock.sbalt[sx, sy, sz, 1] = true; }*/
                                        }
                                        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                                        
                                    }
                                    else
                                    {
                                        if (objectlist[workblock.type].type == "Gas block")
                                        {
                                            checkblock.sbvis[sx, sy, sz, svis] = true;
                                            /*if (objectlist[checkblock.sbtype[sx, sy, sz]].type == "liguid block" & checkblock.sbalt[sx, sy, sz, svis])
                                            {
                                                m[workX, workY].Map[workx, worky, workz] = new strmesh(true);
                                                m[workX, workY].Map[workx, worky, workz].type = workblock.type;
                                                for (byte ssx = 0; ssx < 4; ssx++)
                                                {
                                                    for (byte ssy = 0; ssy < 4; ssy++)
                                                    {
                                                        for (sbyte ssz = 3; ssz >= 0; ssz--)
                                                        {
                                                            m[workX, workY].Map[workx, worky, workz].sbtype[ssx,ssy,ssz] = workblock.type;
                                                        }
                                                    }
                                                }
                                                workblock = m[workX, workY].Map[workx, worky, workz];
                                                //goto checksb;
                                            }*/
                                            if (objectlist[checkblock.type].type == "liguid block")
                                            {

                                                if (objectlist[workblock.type].type == "Gas block" & checkblock.sbtach[sx, sy, sz, svis, 2] > 0)
                                                {
                                                    sbyte wsz2 = (sbyte)(wsz - 1);
                                                    sbyte workz2 = workz;

                                                    if (wsz2 < 0)
                                                    {
                                                        wsz2 = (sbyte)((wsz2 + 4) % 4);
                                                        workz2--;
                                                        if (workz2 < 0)
                                                        {
                                                            wsz2 = wsz;
                                                            workz2 = workz;
                                                        }
                                                    }
                                                    strmesh workblock2 = m[workX, workY].Map[workx, worky, workz2];
                                                    sbyte workx2 = workx;
                                                    sbyte worky2 = worky;
                                                    sbyte workX2 = (sbyte)workX;
                                                    sbyte workY2 = (sbyte)workY;
                                                    sbyte wsx2 = (sbyte)(wsx + shifting[svis].X);
                                                    sbyte wsy2 = (sbyte)(wsy + shifting[svis].Y);
                                                    if (wsx2 < 0 | wsx2 > 3)
                                                    {
                                                        wsx2 = (sbyte)((wsx2 + 4) % 4);
                                                        workx2 += shifting[svis].X;
                                                        if (workx2 < 0 | workx2 > 63)
                                                        {
                                                            workx2 = (sbyte)((workx2 + 64) % 64);
                                                            workX2 += shifting[svis].X;
                                                            if (workX2 < 0 | workX2 > 3)
                                                            {
                                                                wsx2 = wsx;
                                                                workx2 = workx;
                                                                workX2 = (sbyte)workX;
                                                            }
                                                        }
                                                    }

                                                    if (wsy2 < 0 | wsy2 > 3)
                                                    {
                                                        wsy2 = (sbyte)((wsy2 + 4) % 4);
                                                        worky2 += shifting[svis].Y;
                                                        if (worky2 < 0 | worky2 > 63)
                                                        {
                                                            worky2 = (sbyte)((worky2 + 64) % 64);
                                                            workY2 += shifting[svis].Y;
                                                            if (workY2 < 0 | workY2 > 3)
                                                            {
                                                                wsy2 = wsy;
                                                                worky2 = worky;
                                                                workY2 = (sbyte)workY;
                                                            }
                                                        }
                                                    }
                                                    strmesh workblock3 = m[workX2, workY2].Map[workx2, worky2, workz2];
                                                    bool tryy = false;
                                                    if (svis > 1)
                                                    {

                                                        if (workblock2.sb)
                                                        {
                                                            if (objectlist[workblock2.sbtype[wsx, wsy, wsz2]].type != "Gas block")
                                                            {
                                                                if (workblock3.sb)
                                                                {
                                                                    if (objectlist[workblock3.sbtype[wsx2, wsy2, wsz2]].type != "Gas block")
                                                                        tryy = true;
                                                                }
                                                                else
                                                                {
                                                                    if (objectlist[workblock3.type].type != "Gas block")
                                                                        tryy = true;
                                                                }
                                                            }
                                                        }
                                                        else
                                                        {
                                                            if (objectlist[workblock2.type].type != "Gas block")
                                                            {
                                                                if (workblock3.sb)
                                                                {
                                                                    if (objectlist[workblock3.sbtype[wsx2, wsy2, wsz2]].type != "Gas block")
                                                                        tryy = true;
                                                                }
                                                                else
                                                                {
                                                                    if (objectlist[workblock3.type].type != "Gas block")
                                                                        tryy = true;
                                                                }
                                                            }
                                                        }
                                                        if (sz == 0)
                                                        {
                                                            if (workx == cbx & worky == cby & workX == pX & workY == pY)
                                                                tryy = true;
                                                        }
                                                    }
                                                    else tryy = true;
                                                    if (tryy)
                                                    {
                                                        workblock = new strmesh(true);
                                                        workblock.type = checkblock.sbtype[sx, sy, sz];
                                                        workblock.bvis = true;

                                                        for (byte cbSb_X = 0; cbSb_X < 3; cbSb_X++)
                                                        {
                                                            for (byte cbSb_Y = 0; cbSb_Y < 3; cbSb_Y++)
                                                            {
                                                                for (byte cbSb_Z = 0; cbSb_Z < 3; cbSb_Z++)
                                                                {
                                                                    for (byte cbSCH = 0; cbSCH < 6; cbSCH++)
                                                                    {
                                                                        workblock.sbvis[cbSb_X, cbSb_Y, cbSb_Z, cbSCH] = true;
                                                                        workblock.sbtach[cbSb_X, cbSb_Y, cbSb_Z, cbSCH, 0] = m[workX, workY].Map[workx, worky, workz].taches[0, cbSCH];
                                                                    }
                                                                }
                                                            }
                                                        }
                                                        //m[workX, workY].Map[workx, worky, workz].hill = objectlist[checkblock.type].standartHill;
                                                        m[workX, workY].Map[workx, worky, workz] = workblock;
                                                        goto cksmwksm;
                                                    }
                                                }
                                            }
                                        }
                                        else checkblock.sbvis[sx, sy, sz, svis] = false;
                                        checkblock.sbtach[sx, sy, sz, svis, 1] = (byte)(workblock.taches[0, (svis / 2) * 2 + (svis + 1) % 2]);
                                    }
                                }
                                if (checkblock.sbvis[sx, sy, sz, svis] == true) ckvis = true;
                                if (trutach)
                                {
                                    if (checkblock.sbtype[sx, sy, sz] == 5)
                                    {
                                        if (checkblock.sbtach[sx, sy, sz, svis, 1] > 8 & sune > 200 & rand.Next(992) + (checkblock.sbtach[sx, sy, sz, svis, 1] - 8) * 5 > 990)
                                            checkblock.sbalt[sx, sy, sz, svis] = true;
                                        if (checkblock.sbtach[sx, sy, sz, svis, 1] < 5 & rand.Next(1000) > 990)
                                            checkblock.sbalt[sx, sy, sz, svis] = false;
                                    }
                                }
                            }
                        }
                }
                if (!ck)// складывание маленького блока в большой
                {
                    checkblock = new strmesh(false);
                    for (byte sh = 0; sh < 6; sh++)
                    {
                        checkblock.vis[sh] = true;
                        checkblock.taches[1, sh] = 20;
                    }
                    checkblock.type = typeb;

                }
            }
            checkblock.bvis = (ckvis & checkblock.type != 0) ? true : false;
            return checkblock;
        }

        private strmesh checkblocklooprev(byte pX, byte pY, byte cbx, byte cby, byte sbz, bool trutach)
        {
            strmesh checkblock = m[pX, pY].Map[cbx, cby, sbz];
            if (checkblock.type != 0)
            {
                if (checkblock.hill <= 0) //если НР блока = 0, уничтожаем
                {
                    drop[droped].type = checkblock.type;
                    drop[droped].col = 64;
                    drop[droped].time = 0;
                    drop[droped].position = new Vector3(pX * 512, pY * 512, 0) + new Vector3(cbx * 8 + 4, cby * 8 + 4, sbz * 8 + 4);
                    drop[droped].acceleration = new Vector3(0, 0, 1);
                    droped++;
                    checkblock = new strmesh(false);

                   
                }
                else
                {
                    if (checkblock.hill > objectlist[checkblock.type].standartHill)
                    {

                    }
                }
            }
            bool ckvis = false;
            if (!checkblock.sb) //если проверяемый блок - целый
            {
              
                byte[] tc3 = new byte[6];
                for (byte s = 0; s < 6; s++)
                {
                    tc3[s] = checkblock.taches[2, s];
                }
                checkblock.taches = new byte[3, 6];
                for (byte s = 0; s < 6; s++)
                {
                    checkblock.taches[2, s] = tc3[s];
                }
               
                //byte colvis = 0;
                for (byte vis = 0; vis < 6; vis++)
                {
                    if (checkblock.type == 7) { if (!checkblock.alt[vis]) checkblock.taches[0, vis] = 15; }
                    byte workX = pX;
                    byte workY = pY;
                    sbyte workx = (sbyte)(cbx + shifting[vis].X);
                    if (workx > 63) { if (workX < 3) { workx -= 64; workX++; } else workx = 63; } else if (workx < 0) { if (workX > 0) { workx += 64; workX--; } else workx = 0; }
                    sbyte worky = (sbyte)(cby + (int)shifting[vis].Y);
                    if (worky > 63) { if (workY < 3) { worky -= 64; workY++; } else worky = 63; } else if (worky < 0) { if (workY > 0) { worky += 64; workY--; } else worky = 0; }
                    sbyte workz = (sbyte)(sbz + (int)shifting[vis].Z);
                    if (workz >= 64) { workz = 63; } else if (workz < 0) { workz = 0; }
                    strmesh workblock = m[workX, workY].Map[workx, worky, workz];
                ckbigwksm: if (!workblock.sb)  //если с боку полный блок
                    {
                        checkblock.vis[vis] = (workblock.type != 0 | checkblock.type == 0) ? false : true; //и не пустой  //сторона проверяемого блока не видима
                        if (objectlist[checkblock.type].type == "liguid block")
                        {
                            if (objectlist[workblock.type].type == "Gas block" & checkblock.taches[2, vis] > 0)
                            {
                                workblock = new strmesh(true);
                                workblock.type = checkblock.type;
                                workblock.bvis = true;

                                for (byte cbSb_X = 0; cbSb_X < 3; cbSb_X++)
                                {
                                    for (byte cbSb_Y = 0; cbSb_Y < 3; cbSb_Y++)
                                    {
                                        for (byte cbSb_Z = 0; cbSb_Z < 3; cbSb_Z++)
                                        {
                                            for (byte cbSCH = 0; cbSCH < 6; cbSCH++)
                                            {
                                                workblock.sbvis[cbSb_X, cbSb_Y, cbSb_Z, cbSCH] = true;
                                                workblock.sbtach[cbSb_X, cbSb_Y, cbSb_Z, cbSCH, 0] = m[workX, workY].Map[workx, worky, workz].taches[0, cbSCH];
                                            }
                                        }
                                    }
                                }
                                //m[workX, workY].Map[workx, worky, workz].hill = objectlist[checkblock.type].standartHill;
                                m[workX, workY].Map[workx, worky, workz] = workblock;
                                goto ckbigwksm;
                            }
                            else
                            {
                                if (!workblock.sb & objectlist[workblock.type].type == "liguid block")
                                {
                                    for (byte sh = 1; sh < 6; sh++)
                                    {
                                        if ((workblock.taches[2, sh] < checkblock.taches[2, sh] - 4 & vis != 1) | (workblock.taches[2, sh] < checkblock.taches[2, sh] + 4 & vis == 1))
                                        {
                                            m[workX, workY].Map[workx, worky, workz].taches[2, sh] = (vis == 1) ? (byte)(checkblock.taches[2, sh] + 4) : (byte)(checkblock.taches[2, sh] - 4);
                                        }
                                    }
                                    if (vis == 0 & workblock.taches[2, 0] < checkblock.taches[2, 0] - 4)
                                    {
                                        m[workX, workY].Map[workx, worky, workz].taches[2, 0] = (byte)(checkblock.taches[2, 0] - 4);
                                    }
                                }
                               
                            }
                            
                        }
                        if (workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2] > 0)
                        {
                            if (checkblock.type == 0)
                            {
                                for (byte sh = 0; sh < 6; sh++)
                                {
                                    if (vis == 0 & workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2] == 20) { checkblock.taches[0, sh] = (byte)(workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2]); }
                                    else if (workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2] - 2 > checkblock.taches[0, sh]) checkblock.taches[0, sh] = (byte)(workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2] - 2);
                                }
                            }
                            else
                            {
                                checkblock.taches[1, vis] = workblock.taches[0, (vis / 2) * 2 + (vis + 1) % 2];
                            }
                        }
                       
                    }
                    else//если с боку неполный блок
                    {
                        checkblock.vis[vis] = true;

                        sbyte wsx = 0;
                        sbyte wfx = 0;
                        sbyte wsy = 0;
                        sbyte wfy = 0;
                        sbyte wsz = 0;
                        sbyte wfz = 0;
                        if (shifting[vis].X == 0)
                        {
                            wsx = 0;
                            wfx = 3;
                        }
                        else
                        {
                            wsx = (sbyte)((shifting[vis].X + 4) % 5);
                            wfx = wsx;
                        }
                        if (shifting[vis].Y == 0)
                        {
                            wsy = 0;
                            wfy = 3;
                        }
                        else
                        {
                            wsy = (sbyte)((shifting[vis].Y + 4) % 5);
                            wfy = wsy;
                        }
                        if (shifting[vis].Z == 0)
                        {
                            wsz = 0;
                            wfz = 3;
                        }
                        else
                        {
                            wsz = (sbyte)((shifting[vis].Z + 4) % 5);
                            wfz = wsz;
                        }
                        for (sbyte sx =wfx ; sx >= wsx; sx--)
                        {
                            for (sbyte sy = wfy; sy >= wsy; sy--)
                            {
                                for (sbyte sz = wfz; sz >= wsz; sz--)
                                {


                                    if (objectlist[checkblock.type].type == "Gas block")
                                    {
                                        for (byte sh = 0; sh < 6; sh++)
                                        {
                                            if (vis == 0 & workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0] == 20) checkblock.taches[0, sh] = (byte)(workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0]);
                                            else if (workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0] - 2 > checkblock.taches[0, sh]) checkblock.taches[0, sh] = (byte)(workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0] - 2);
                                        }
                                    }
                                    else
                                    {
                                        if (objectlist[checkblock.type].type == "liguid block")
                                        {
                                            sbyte workz2 = workz;
                                            sbyte z2wb = (sbyte)(sz - 1);
                                            if (z2wb < 0)
                                            {
                                                workz2--;
                                                z2wb += 4;
                                            }
                                            if (workz2 < 0)
                                            {
                                                workz2 = workz;
                                                z2wb = sz;
                                            }
                                            strmesh workblock2 = m[workX, workY].Map[workx, worky, workz2]; //на один маленький блок ниже рабочего
                                            //strmesh workblock3 = m[workX, workY].Map[workx + shifting[vis].X, worky + shifting[vis].Y, workz2]; //на один маленький блок ниже рабочего и на один вперёд
                                            if (objectlist[workblock.sbtype[sx, sy, sz]].type == "Gas block")
                                            {
                                                if (workblock2.sb)
                                                {
                                                    if (((vis > 1 & ((objectlist[workblock2.sbtype[sx, sy, z2wb]].type != "Gas block" & objectlist[workblock2.sbtype[sx + shifting[vis].X, sy + shifting[vis].Y, z2wb]].type != "Gas block") | sz == 0)) | vis <= 1) & checkblock.taches[2, vis] > 0)
                                                    {
                                                        //m[workX, workY].Map[workx, worky, workz] = new strmesh(false);
                                                        m[workX, workY].Map[workx, worky, workz].sbtype[sx, sy, sz] = checkblock.type;
                                                        for (byte sh = 0; sh < 6; sh++)
                                                        {
                                                            m[workX, workY].Map[workx, worky, workz].sbtach[sx, sy, sz, sh, 0] = 0;
                                                            m[workX, workY].Map[workx, worky, workz].sbtach[sx, sy, sz, sh, 1] = checkblock.taches[1, vis];
                                                            m[workX, workY].Map[workx, worky, workz].sbvis[sx, sy, sz, sh] = true;
                                                            //if(sh!=0) m[workX, workY].Map[workx, worky, workz].sbtach[sx, sy, sz, sh, 2] = (byte)(checkblock.taches[2, vis] - 1);
                                                        }
                                                        //m[workX, workY].Map[workx, worky, workz].hill += (short)(objectlist[checkblock.type].standartHill/64);
                                                        workblock = m[workX, workY].Map[workx, worky, workz];
                                                    }
                                                }
                                                else
                                                {
                                                    if (((vis > 1 & (objectlist[workblock2.type].type != "Gas block" | sz == 0)) | (vis <= 1)) & checkblock.taches[2, vis] > 0)
                                                    {
                                                        //m[workX, workY].Map[workx, worky, workz] = new strmesh(false);
                                                        m[workX, workY].Map[workx, worky, workz].sbtype[sx, sy, sz] = checkblock.type;
                                                        for (byte sh = 0; sh < 6; sh++)
                                                        {
                                                            m[workX, workY].Map[workx, worky, workz].sbtach[sx, sy, sz, sh, 0] = 0;
                                                            m[workX, workY].Map[workx, worky, workz].sbtach[sx, sy, sz, sh, 1] = checkblock.taches[1, vis];
                                                            m[workX, workY].Map[workx, worky, workz].sbvis[sx, sy, sz, sh] = true;
                                                            //if(sh!=0) m[workX, workY].Map[workx, worky, workz].sbtach[sx, sy, sz, sh, 2] = (byte)(checkblock.taches[2, vis] - 1);
                                                        }
                                                        //m[workX, workY].Map[workx, worky, workz].hill += (short)(objectlist[checkblock.type].standartHill/64);
                                                        workblock = m[workX, workY].Map[workx, worky, workz];
                                                    }
                                                }
                                            }
                                            else
                                            {

                                                if (objectlist[workblock.sbtype[sx, sy, sz]].type == "liguid block")
                                                {
                                                    for (byte sh = 1; sh < 6; sh++)
                                                    {
                                                        if ((workblock.sbtach[sx, sy, sz, sh, 2] < checkblock.taches[2, sh] - 1 & vis != 1) | (workblock.sbtach[sx, sy, sz, sh, 2] < checkblock.taches[2, sh] + 1 & vis == 1))
                                                            m[workX, workY].Map[workx, worky, workz].sbtach[sx, sy, sz, sh, 2] = (vis == 1) ? (byte)(checkblock.taches[2, sh] + 1) : (byte)(checkblock.taches[2, sh] - 1);
                                                    }
                                                    if (vis == 0 & workblock.sbtach[sx, sy, sz, 0, 2] < checkblock.taches[2, 0] - 1)
                                                    {
                                                        m[workX, workY].Map[workx, worky, workz].sbtach[sx, sy, sz, 0, 2] = (byte)(checkblock.taches[2, 0] - 1);
                                                    }
                                                    //workblock = m[workX, workY].Map[workx, worky, workz];
                                                }
                                            }
                                            

                                        }
                                        if (checkblock.taches[1, vis] < workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0])
                                        { checkblock.taches[1, vis] = workblock.sbtach[sx, sy, sz, (vis / 2) * 2 + (vis + 1) % 2, 0]; }
                                    }
                                }
                            }
                        }
                    }
                    //  if (workM[workX, workY].Map[workx, worky, workz].taches[0, (vis / 2) * 2 + (vis +1) % 2] > 0)
                    // {
                    if (checkblock.vis[vis] == true) ckvis = true;
                    if (trutach)
                    {
                        if (checkblock.type == 5)
                        {
                            //if(x==7 & y==7 & z==23){}
                            if (checkblock.taches[1, vis] > 8 & sune > 200 & rand.Next(992) + (checkblock.taches[1, vis] - 8) * 2 > 990)
                                checkblock.alt[vis] = true;
                            if (checkblock.taches[1, vis] < 5 & rand.Next(1000) > 990)
                                checkblock.alt[vis] = false;
                        }
                    }

                    // if (checkblock.vis[vis]) colvis++;
                }
                //  if (objectlist[checkblock.type].type == "liguid block")
                

            }
            else
            {
                bool ck = false;
                byte typeb = checkblock.sbtype[0, 0, 0];
                for (sbyte sx = 3; sx >= 0; sx--)
                {
                    for (sbyte sy = 3; sy >= 0; sy--)
                    {
                        for (sbyte sz = 3; sz >= 0; sz--)
                        {
                            for (byte svis = 0; svis < 6; svis++)
                            {
                                checkblock.sbtach[sx, sy, sz, svis, 0] = 0;
                                checkblock.sbtach[sx, sy, sz, svis, 1] = 0;
                            }
                            if (typeb != checkblock.sbtype[sx, sy, sz]) ck = true;
                            for (byte svis = 0; svis < 6; svis++)
                            {

                                checkblock.sbvis[sx, sy, sz, svis] = true;
                                //checkblock.sbtach[sx, sy, sz, svis, 1] = 10;
                                byte workX = pX;
                                byte workY = pY;
                                sbyte workx = (sbyte)cbx;
                                sbyte worky = (sbyte)cby;
                                sbyte workz = (sbyte)sbz;
                                sbyte wsx = (sbyte)(sx + shifting[svis].X);
                                sbyte wsy = (sbyte)(sy + shifting[svis].Y);
                                sbyte wsz = (sbyte)(sz + shifting[svis].Z);
                                if (wsx < 0 | wsx > 3)
                                {
                                    wsx = (sbyte)((wsx + 4) % 4);//!!!!!!!!!!!!!!!!!!!!!!!
                                    workx = (sbyte)(cbx + shifting[svis].X);
                                    if (workx > 63) { if (workX < 3) { workx -= 64; workX++; } else workx = 63; } else if (workx < 0) { if (workX > 0) { workx += 64; workX--; } else workx = 0; }
                                }
                                if (wsy < 0 | wsy > 3)
                                {
                                    wsy = (sbyte)((wsy + 4) % 4);
                                    worky = (sbyte)(cby + (int)shifting[svis].Y);
                                    if (worky > 63) { if (workY < 3) { worky -= 64; workY++; } else worky = 63; } else if (worky < 0) { if (workY > 0) { worky += 64; workY--; } else worky = 0; }
                                }
                                if (wsz < 0 | wsz > 3)
                                {
                                    wsz = (sbyte)((wsz + 4) % 4);
                                    workz = (sbyte)(sbz + (int)shifting[svis].Z);
                                    if (workz >= 64) { workz = 63; } else if (workz < 0) { workz = 0; }
                                }
                                strmesh workblock = m[workX, workY].Map[workx, worky, workz];

                                if (checkblock.sbtype[sx, sy, sz] == 0)
                                {
                                    checkblock.sbvis[sx, sy, sz, svis] = false;
                                    for (byte sh = 0; sh < 6; sh++)
                                    {
                                        if (workblock.sb)
                                        {
                                            if (svis == 0 & workblock.sbtach[sx, sy, (sz + 1) % 4, (svis / 2) * 2 + (svis + 1) % 2, 0] == 20) { checkblock.sbtach[sx, sy, sz, sh, 0] = (byte)(workblock.sbtach[sx, sy, (sz + 1) % 4, (svis / 2) * 2 + (svis + 1) % 2, 0]); }
                                            else if (workblock.sbtach[wsx, wsy, wsz, (svis / 2) * 2 + (svis + 1) % 2, 0] - 2 > checkblock.sbtach[sx, sy, sz, sh, 0]) checkblock.sbtach[sx, sy, sz, sh, 0] = (byte)(workblock.sbtach[wsx, wsy, wsz, (svis / 2) * 2 + (svis + 1) % 2, 0] - 2);
                                        }
                                        else
                                        {
                                            if (svis == 0 & workblock.taches[0, (svis / 2) * 2 + (svis + 1) % 2] == 20) { checkblock.sbtach[sx, sy, sz, sh, 0] = (byte)(workblock.taches[0, (svis / 2) * 2 + (svis + 1) % 2]); }
                                            else if ((workblock.taches[0, (svis / 2) * 2 + (svis + 1) % 2] - 2) > checkblock.sbtach[sx, sy, sz, sh, 0]) checkblock.sbtach[sx, sy, sz, sh, 0] = (byte)(workblock.taches[0, (svis / 2) * 2 + (svis + 1) % 2] - 2);
                                            //if (checkblock.sbtach[sx, sy, sz, sh, 0] > 20) checkblock.sbtach[sx, sy, sz, sh, 0] = 20;
                                            //checkblock.sbtach[sx, sy, sz, sh, 0] = (byte)(workblock.taches[(svis / 2) * 2 + (svis + 1) % 2, 0]/4);
                                        }
                                    }
                                }
                                else
                                {

                                cksmwksm: if (workblock.sb)
                                    {
                                        if (objectlist[workblock.sbtype[wsx, wsy, wsz]].type == "Gas block") checkblock.sbvis[sx, sy, sz, svis] = true; else checkblock.sbvis[sx, sy, sz, svis] = false;
                                        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                                        if (objectlist[checkblock.sbtype[sx, sy, sz]].type == "liguid block")
                                        {
                                            if (objectlist[workblock.sbtype[wsx, wsy, wsz]].type == "Gas block")
                                            {
                                                sbyte wsz2 = (sbyte)(wsz - 1);
                                                sbyte workz2 = workz;

                                                if (wsz2 < 0)
                                                {
                                                    wsz2 = (sbyte)((wsz2 + 4) % 4);
                                                    workz2--;
                                                    if (workz2 < 0)
                                                    {
                                                        wsz2 = wsz;
                                                        workz2 = workz;
                                                    }
                                                }
                                                strmesh workblock2 = m[workX, workY].Map[workx, worky, workz2];
                                                sbyte workx2 = workx;
                                                sbyte worky2 = worky;
                                                sbyte workX2 = (sbyte)workX;
                                                sbyte workY2 = (sbyte)workY;
                                                sbyte wsx2 = (sbyte)(wsx + shifting[svis].X);
                                                sbyte wsy2 = (sbyte)(wsy + shifting[svis].Y);
                                                if (wsx2 < 0 | wsx2 > 3)
                                                {
                                                    wsx2 = (sbyte)((wsx2 + 4) % 4);
                                                    workx2 += shifting[svis].X;
                                                    if (workx2 < 0 | workx2 > 63)
                                                    {
                                                        workx2 = (sbyte)((workx2 + 64) % 64);
                                                        workX2 += shifting[svis].X;
                                                        if (workX2 < 0 | workX2 > 3)
                                                        {
                                                            wsx2 = wsx;
                                                            workx2 = workx;
                                                            workX2 = (sbyte)workX;
                                                        }
                                                    }
                                                }

                                                if (wsy2 < 0 | wsy2 > 3)
                                                {
                                                    wsy2 = (sbyte)((wsy2 + 4) % 4);
                                                    worky2 += shifting[svis].Y;
                                                    if (worky2 < 0 | worky2 > 63)
                                                    {
                                                        worky2 = (sbyte)((worky2 + 64) % 64);
                                                        workY2 += shifting[svis].Y;
                                                        if (workY2 < 0 | workY2 > 3)
                                                        {
                                                            wsy2 = wsy;
                                                            worky2 = worky;
                                                            workY2 = (sbyte)workY;
                                                        }
                                                    }
                                                }
                                                strmesh workblock3 = m[workX2, workY2].Map[workx2, worky2, workz2];
                                                bool tryy = false;
                                                if (svis > 1)
                                                {
                                                    if (workblock2.sb)
                                                    {
                                                        if (objectlist[workblock2.sbtype[wsx, wsy, wsz2]].type != "Gas block")
                                                        {
                                                            if (workblock3.sb)
                                                            {
                                                                if (objectlist[workblock3.sbtype[wsx2, wsy2, wsz2]].type != "Gas block")
                                                                    tryy = true;
                                                            }
                                                            else
                                                            {
                                                                if (objectlist[workblock3.type].type != "Gas block")
                                                                    tryy = true;
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {
                                                        if (objectlist[workblock2.type].type != "Gas block")
                                                        {
                                                            if (workblock3.sb)
                                                            {
                                                                if (objectlist[workblock3.sbtype[wsx2, wsy2, wsz2]].type != "Gas block")
                                                                    tryy = true;
                                                            }
                                                            else
                                                            {
                                                                if (objectlist[workblock3.type].type != "Gas block")
                                                                    tryy = true;
                                                            }
                                                        }
                                                    }

                                                    if (sz == 0)
                                                    {
                                                        if (workx == cbx & worky == cby & workX == pX & workY == pY)
                                                            tryy = true;
                                                    }
                                                }
                                                else tryy = true;

                                                if (checkblock.sbtach[sx, sy, sz, svis, 2] > 0 & tryy)
                                                {
                                                    m[workX, workY].Map[workx, worky, workz].sbtype[wsx, wsy, wsz] = checkblock.sbtype[sx, sy, sz];
                                                    for (byte sh = 0; sh < 6; sh++)
                                                    {
                                                        m[workX, workY].Map[workx, worky, workz].sbtach[wsx, wsy, wsz, sh, 0] = 0;
                                                        m[workX, workY].Map[workx, worky, workz].sbtach[wsx, wsy, wsz, sh, 1] = checkblock.sbtach[sx, sy, sz, svis, 1];
                                                        m[workX, workY].Map[workx, worky, workz].sbvis[wsx, wsy, wsz, sh] = true;
                                                        //if (sh!=0) m[workX, workY].Map[workx, worky, workz].sbtach[wsx, wsy, wsz, sh, 2] = (byte)(checkblock.sbtach[sx, sy, sz,svis,2]-1); 
                                                    }
                                                    
                                                    workblock = m[workX, workY].Map[workx, worky, workz];
                                                }
                                            }
                                            else
                                            {
                                                if (objectlist[workblock.sbtype[wsx, wsy, wsz]].type == "liguid block")
                                                {
                                                    for (byte sh = 1; sh < 6; sh++)
                                                    {
                                                        if ((workblock.sbtach[wsx, wsy, wsz, sh, 2] < checkblock.sbtach[sx, sy, sz, sh, 2] - 1 & svis != 1) | (workblock.sbtach[wsx, wsy, wsz, sh, 2] < checkblock.sbtach[sx, sy, sz, sh, 2] + 1) & svis == 1)
                                                            m[workX, workY].Map[workx, worky, workz].sbtach[wsx, wsy, wsz, sh, 2] = (svis == 1) ? (byte)(checkblock.sbtach[sx, sy, sz, sh, 2] + 1) : (byte)(checkblock.sbtach[sx, sy, sz, sh, 2] - 1);
                                                    }
                                                    if (svis == 0 & workblock.sbtach[wsx, wsy, wsz, 0, 2] < checkblock.sbtach[sx, sy, sz, 0, 2] - 1)
                                                    {
                                                        m[workX, workY].Map[workx, worky, workz].sbtach[wsx, wsy, wsz, 0, 2] = (byte)(checkblock.sbtach[sx, sy, sz, 0, 2] - 1);
                                                    }
                                                }
                                            }

                                            
                                        }
                                        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                                        checkblock.sbtach[sx, sy, sz, svis, 1] = (byte)(workblock.sbtach[wsx, wsy, wsz, (svis / 2) * 2 + (svis + 1) % 2, 0] );
                                    }
                                    else
                                    {
                                        if (objectlist[workblock.type].type == "Gas block")
                                        {
                                            checkblock.sbvis[sx, sy, sz, svis] = true;
                                           
                                            if (objectlist[checkblock.type].type == "liguid block")
                                            {

                                                if (objectlist[workblock.type].type == "Gas block" & checkblock.sbtach[sx, sy, sz, svis, 2] > 0)
                                                {
                                                    sbyte wsz2 = (sbyte)(wsz - 1);
                                                    sbyte workz2 = workz;

                                                    if (wsz2 < 0)
                                                    {
                                                        wsz2 = (sbyte)((wsz2 + 4) % 4);
                                                        workz2--;
                                                        if (workz2 < 0)
                                                        {
                                                            wsz2 = wsz;
                                                            workz2 = workz;
                                                        }
                                                    }
                                                    strmesh workblock2 = m[workX, workY].Map[workx, worky, workz2];
                                                    sbyte workx2 = workx;
                                                    sbyte worky2 = worky;
                                                    sbyte workX2 = (sbyte)workX;
                                                    sbyte workY2 = (sbyte)workY;
                                                    sbyte wsx2 = (sbyte)(wsx + shifting[svis].X);
                                                    sbyte wsy2 = (sbyte)(wsy + shifting[svis].Y);
                                                    if (wsx2 < 0 | wsx2 > 3)
                                                    {
                                                        wsx2 = (sbyte)((wsx2 + 4) % 4);
                                                        workx2 += shifting[svis].X;
                                                        if (workx2 < 0 | workx2 > 63)
                                                        {
                                                            workx2 = (sbyte)((workx2 + 64) % 64);
                                                            workX2 += shifting[svis].X;
                                                            if (workX2 < 0 | workX2 > 3)
                                                            {
                                                                wsx2 = wsx;
                                                                workx2 = workx;
                                                                workX2 = (sbyte)workX;
                                                            }
                                                        }
                                                    }

                                                    if (wsy2 < 0 | wsy2 > 3)
                                                    {
                                                        wsy2 = (sbyte)((wsy2 + 4) % 4);
                                                        worky2 += shifting[svis].Y;
                                                        if (worky2 < 0 | worky2 > 63)
                                                        {
                                                            worky2 = (sbyte)((worky2 + 64) % 64);
                                                            workY2 += shifting[svis].Y;
                                                            if (workY2 < 0 | workY2 > 3)
                                                            {
                                                                wsy2 = wsy;
                                                                worky2 = worky;
                                                                workY2 = (sbyte)workY;
                                                            }
                                                        }
                                                    }
                                                    strmesh workblock3 = m[workX2, workY2].Map[workx2, worky2, workz2];
                                                    bool tryy = false;
                                                    if (svis > 1)
                                                    {

                                                        if (workblock2.sb)
                                                        {
                                                            if (objectlist[workblock2.sbtype[wsx, wsy, wsz2]].type != "Gas block")
                                                            {
                                                                if (workblock3.sb)
                                                                {
                                                                    if (objectlist[workblock3.sbtype[wsx2, wsy2, wsz2]].type != "Gas block")
                                                                        tryy = true;
                                                                }
                                                                else
                                                                {
                                                                    if (objectlist[workblock3.type].type != "Gas block")
                                                                        tryy = true;
                                                                }
                                                            }
                                                        }
                                                        else
                                                        {
                                                            if (objectlist[workblock2.type].type != "Gas block")
                                                            {
                                                                if (workblock3.sb)
                                                                {
                                                                    if (objectlist[workblock3.sbtype[wsx2, wsy2, wsz2]].type != "Gas block")
                                                                        tryy = true;
                                                                }
                                                                else
                                                                {
                                                                    if (objectlist[workblock3.type].type != "Gas block")
                                                                        tryy = true;
                                                                }
                                                            }
                                                        }
                                                        if (sz == 0)
                                                        {
                                                            if (workx == cbx & worky == cby & workX == pX & workY == pY)
                                                                tryy = true;
                                                        }
                                                    }
                                                    else tryy = true;
                                                    if (tryy)
                                                    {
                                                        workblock = new strmesh(true);
                                                        workblock.type = checkblock.sbtype[sx, sy, sz];
                                                        workblock.bvis = true;

                                                        for (byte cbSb_X = 0; cbSb_X < 3; cbSb_X++)
                                                        {
                                                            for (byte cbSb_Y = 0; cbSb_Y < 3; cbSb_Y++)
                                                            {
                                                                for (byte cbSb_Z = 0; cbSb_Z < 3; cbSb_Z++)
                                                                {
                                                                    for (byte cbSCH = 0; cbSCH < 6; cbSCH++)
                                                                    {
                                                                        workblock.sbvis[cbSb_X, cbSb_Y, cbSb_Z, cbSCH] = true;
                                                                        workblock.sbtach[cbSb_X, cbSb_Y, cbSb_Z, cbSCH, 0] = m[workX, workY].Map[workx, worky, workz].taches[0, cbSCH];
                                                                    }
                                                                }
                                                            }
                                                        }
                                                        //m[workX, workY].Map[workx, worky, workz].hill = objectlist[checkblock.type].standartHill;
                                                        m[workX, workY].Map[workx, worky, workz] = workblock;
                                                        goto cksmwksm;
                                                    }
                                                }
                                            }
                                        }
                                        else checkblock.sbvis[sx, sy, sz, svis] = false;
                                        checkblock.sbtach[sx, sy, sz, svis, 1] = (byte)(workblock.taches[0, (svis / 2) * 2 + (svis + 1) % 2]);
                                    }
                                }
                                if (checkblock.sbvis[sx, sy, sz, svis] == true) ckvis = true;
                                if (trutach)
                                {
                                    if (checkblock.sbtype[sx, sy, sz] == 5)
                                    {
                                        if (checkblock.sbtach[sx, sy, sz, svis, 1] > 8 & sune > 200 & rand.Next(992) + (checkblock.sbtach[sx, sy, sz, svis, 1] - 8) * 5 > 990)
                                            checkblock.sbalt[sx, sy, sz, svis] = true;
                                        if (checkblock.sbtach[sx, sy, sz, svis, 1] < 5 & rand.Next(1000) > 990)
                                            checkblock.sbalt[sx, sy, sz, svis] = false;
                                    }
                                }
                            }
                        }
                    }
                }
                if (!ck)// складывание маленького блока в большой
                {
                    checkblock = new strmesh(false);
                    for (byte sh = 0; sh < 6; sh++)
                    {
                        checkblock.vis[sh] = true;
                        checkblock.taches[1, sh] = 15;
                    }
                    checkblock.type = typeb;

                }
            }
            checkblock.bvis = (ckvis & checkblock.type != 0) ? true : false;
            return checkblock;
        }

        float ee = 0;
        bvec3 precipspeed;  //скорость осадков
        byte preciptype;    //тип осадков
        byte precipintensity;
        private void gametimer_Tick(object sender, EventArgs e)
        {
            





            cpold = cp;
            sune = (sune + shiftsune) % 256;
            if(sune<100 & shiftsune==-0.03f)
            { shiftsune = -0.04f; }
            if (sune < 0) { sune = 0; shiftsune = 0.04f; }
            if (sune > 100 & shiftsune == 0.04f)
            { shiftsune = 0.03f; }
            if (sune > 255) { sune = 255; shiftsune = -0.03f; }

            sun = new Vector3((float)Math.Sin(0) * 200 + 100, (float)Math.Cos(0) * 200 + 100, -(float)Math.Cos(0) * 200 - 100);
            if (going)
            {
                Vector3 cn = ct - cp;
                if (uX > 2 | uY > 2 | uX < 1 | uY < 1 | finishshift == true)
                {
                    if (startshift == false & finishshift == false)
                    {
                        for (int ex = 0; ex < 4; ex++)
                        {
                            for (int ey = 0; ey < 4; ey++)
                            {
                                MapList mm = m[ex, ey];
                                workM[ex, ey] = mm;
                            }
                        }
                        workX = uX;
                        workY = uY;
                        startshift = true;
                    }
                    else if (startshift == false & finishshift == true)
                    {
                        for (int nx = startshiftX; nx != finshiftX; nx += stepX)
                        {
                            for (int ny = startshiftY; ny != finshiftY; ny += stepY)
                            {
                                if (nx + offsetX >= 0 & nx + offsetX < 4 & ny + offsetY >= 0 & ny + offsetY < 4)
                                {
                                    MapList mm = m[nx + offsetX, ny + offsetY];
                                    m[nx, ny] = mm;

                                }
                            }
                        }
                        for (int ns = 0; ns < lload.Count; ns++)
                        {
                            m[lload[ns] / 4, lload[ns] % 4] = workM[lload[ns] / 4, lload[ns] % 4];
                        }
                        uX -= offsetX;
                        uY -= offsetY;
                        pX -= offsetX;
                        pY -= offsetY;
                        p0X -= offsetX;
                        p0Y -= offsetY;
                        finishshift = false;
                    }
                }


                // if (walking)


                /*Просчёт передвижения*/

                // Vector3 cn = ct - cp;       //
                Vector3 cnnz = new Vector3(cn.X, cn.Y, 0);      //единичный вектор по направлению глаз игрока, без учёта направления по вертикали (X,Y,0)
                float corr = 1 / cnnz.Length();
                cnnz.X = cnnz.X * corr;
                cnnz.Y = cnnz.Y * corr;
                Vector3 cnnze = new Vector3(cnnz.Y, cnnz.X, 0); //единичный вектор, перпендикулярный cnnz
                Vector3[, ,] user = new Vector3[4, 4, 9];   //матрица координат тела игрока
                Vector3 vnfb = cn, vnrl;
                vnfb.Z = 0;
                vnfb = Vector3.Normalize(vnfb);
                vnrl = Vector3.Cross(cup, vnfb);
                //Vector3 cts = ct;
                Vector3 acceleration;
                /*EXP*/
                int uXus=0;
                int uYus=0;
                if (stepfb!=0 | steprl!=0)
                for (int step = 0; step < Run * 10; step++)
                {
                    acceleration = (vnfb * stepfb + vnrl * steprl);
                    //acceleration.Z = accelerate;
                    cp0 = cp + acceleration * 1.2f;
                    //cp0.Z += (float)(Math.Pow(accelerate, 2) * Math.Sign(accelerate));                                                                                                        
                    int uXo = uX;
                    int uYo = uY;
                    Vector3 cpo = cp;
                    if (cp0.X < -4 | cp0.X >= 512 | cp0.Y < -4 | cp0.Y >= 512)//если положение головы за рамками текущей матрицы
                    {// меняются координаты текущей матрицы, 
                        // сдвигаются координаты головы

                        if (cp0.X < 0)
                        {
                            uX--;
                            cp0.X += 512;
                            cp.X += 512;
                        }
                        else
                        {
                            if (cp0.X >= 512)
                            {
                                uX++;
                                cp0.X -= 512;
                                cp.X -= 512;
                            }
                        }
                        if (cp0.Y < 0)
                        {
                            uY--;
                            cp0.Y += 512;
                            cp.Y += 512;
                        }
                        else
                        {
                            if (cp0.Y >= 512)
                            {
                                uY++;
                                cp0.Y -= 512;
                                cp.Y -= 512;
                            }
                        }
                    }
                    for (int xe = 0; xe < 4; xe++)
                    {
                        for (int ye = 0; ye < 4; ye++)
                        {
                            user[xe, ye, 0] = cp0 + (xe * 1.5f - 2.25f) * cnnze + (ye * 1.5f - 2.25f) * cnnz;
                            user[xe, ye, 0].Z = cp0.Z - 13;
                        }
                    }

                    for (int xe = 0; xe < 4; xe++)
                    {
                        for (int ye = 0; ye < 4; ye++)
                        {
                            for (int ze = 1; ze < 9; ze++)
                            {
                                user[xe, ye, ze] = user[xe, ye, 0];
                                user[xe, ye, ze].Z = cp0.Z - 13 + ze * 2;
                            }
                        }
                    }

                    bool stop = false;
                    for (int a = 0; a < 4; a++) //проверяем, не уперся ли игрок в блок
                    {
                        for (int b = 0; b < 4; b++)
                        {
                            if ((a != 0 & b != 0) | (a != 0 & b != 3) | (a != 3 & b != 0) | (a != 3 & b != 3))
                                for (int c = 2; c < 8; c++)
                                {
                                    uXus = uX;
                                    uYus = uY;
                                    head.X = (int)(user[a, b, c].X / 8);
                                    if (head.X > 63) { head.X -= 64; uXus += 1; }
                                    else if (head.X < 0) { head.X += 64; uXus -= 1; }
                                    head.Y = (int)(user[a, b, c].Y / 8);
                                    if (head.Y > 63) { head.Y -= 64; uYus += 1; }
                                    else if (head.Y < 0) { head.Y += 64; uYus -= 1; }
                                    head.Z = (int)(user[a, b, c].Z / 8);
                                    int sbx = (int)(user[a, b, c].X - head.X * 8) / 2;
                                    int sby = (int)(user[a, b, c].Y - head.Y * 8) / 2;
                                    int sbz = (int)(user[a, b, c].Z - head.Z * 8) / 2;
                                    if (!m[uXus, uYus].Map[(int)head.X, (int)head.Y, (int)head.Z].sb)
                                    {
                                        if (objectlist[m[uXus, uYus].Map[(int)head.X, (int)head.Y, (int)head.Z].type].type == "block")
                                        {
                                            stop = true;
                                            a = 5;
                                            b = 5;
                                            c = 9;
                                        }
                                    }
                                    else if (m[uXus, uYus].Map[(int)head.X, (int)head.Y, (int)head.Z].sb)
                                    {
                                        if (objectlist[m[uXus, uYus].Map[(int)head.X, (int)head.Y, (int)head.Z].sbtype[sbx, sby, sbz]].type == "block")
                                        {
                                            stop = true;
                                            a = 5;
                                            b = 5;
                                            c = 9;
                                        }
                                    }

                                }
                        }
                    }
                    if (stop)   //если упёрся
                    {
                        acceleration.X = 0;
                        acceleration.Y = 0;
                        Run = walk;
                    }


                    /* if (!stop)
                     {
                         cp += (vnfb * stepfb + vnrl * steprl) * Run;
                         //если перемещение удалось, проверка на необходимость перезаписи и подзагрузки/
                     }
                     else//если перемещение не удалось, координаты становятся прежними/
                     {
                         cp = cpo;
                         uX = uXo;
                         uY = uYo;
                     }*/
                  

                    cp += acceleration * 0.1f;


                    if (cp0.X < -4 | cp0.X >= 512 | cp0.Y < -4 | cp0.Y >= 512)//если положение головы за рамками текущей матрицы
                    {// меняются координаты текущей матрицы,
                        // сдвигаются координаты головы

                        if (cp0.X < 0)
                        {
                            uX--;
                            cp0.X += 512;
                            cp.X += 512;
                        }
                        else
                        {
                            if (cp0.X >= 512)
                            {
                                uX++;
                                cp0.X -= 512;
                                cp.X -= 512;
                            }
                        }
                        if (cp0.Y < 0)
                        {
                            uY--;
                            cp0.Y += 512;
                            cp.Y += 512;
                        }
                        else
                        {
                            if (cp0.Y >= 512)
                            {
                                uY++;
                                cp0.Y -= 512;
                                cp.Y -= 512;
                            }
                        }
                    }
                }
                //********************************************************************

                /*Просчёт прыжка*/
                if (flight)
                {
                    cp.Z += accelerate;
                    accelerate -= 0.1f;

                }
                for (int xe = 0; xe < 4; xe++)
                {
                    for (int ye = 0; ye < 4; ye++)
                    {
                        user[xe, ye, 8] = cp + (xe * 1.2f - 1.8f) * cnnze + (ye * 1.2f - 1.8f) * cnnz;
                        user[xe, ye, 8].Z = cp.Z + 1;
                    }
                }

                for (int xe = 0; xe < 4; xe++)
                {
                    for (int ye = 0; ye < 4; ye++)
                    {
                        user[xe, ye, 0] = user[xe, ye, 8];
                        user[xe, ye, 0].Z = cp.Z - 13.001f;
                        user[xe, ye, 1] = user[xe, ye, 8];
                        user[xe, ye, 1].Z = cp.Z - 12.999f;
                    }
                }
                bool stopp = false;
                
                for (int a = 0; a < 4; a++)
                {
                    for (int b = 0; b < 4; b++)
                    {
                        if ((a != 0 & b != 0) | (a != 0 & b != 3) | (a != 3 & b != 0) | (a != 3 & b != 3))
                        {
                            uXus = uX;
                            uYus = uY;
                            head.X = (int)(user[a, b, 1].X / 8);
                            if (head.X > 63) { head.X -= 64; uXus += 1; }
                            else if (head.X < 0) { head.X += 64; uXus -= 1; }
                            head.Y = (int)(user[a, b, 1].Y / 8);
                            if (head.Y > 63) { head.Y -= 64; uYus += 1; }
                            else if (head.Y < 0) { head.Y += 64; uYus -= 1; }
                            head.Z = (int)(user[a, b, 1].Z / 8);

                            int sbx = (int)(user[a, b, 1].X - head.X * 8) / 2;
                            int sby = (int)(user[a, b, 1].Y - head.Y * 8) / 2;
                            int sbz = (int)(user[a, b, 1].Z - head.Z * 8) / 2;
                            if (!m[uXus, uYus].Map[(int)head.X, (int)head.Y, (int)head.Z].sb)
                            {
                                if (objectlist[m[uXus, uYus].Map[(int)head.X, (int)head.Y, (int)head.Z].type].type == "block" )
                                {
                                    stopp = true;
                                    a = 5;
                                    b = 5;
                                }
                            }
                            else if (m[uXus, uYus].Map[(int)head.X, (int)head.Y, (int)head.Z].sb)
                            {
                                if (objectlist[m[uXus, uYus].Map[(int)head.X, (int)head.Y, (int)head.Z].sbtype[sbx, sby, sbz]].type == "block")
                                {
                                    stopp = true;
                                    a = 5;
                                    b = 5;
                                }
                            }
                        }
                    }
                }
                if (stopp)
                {
                    if (!m[uXus, uYus].Map[(int)head.X, (int)head.Y, (int)head.Z].sb) cp.Z = (flight) ? (head.Z + 1) * 8 + 13 : cp.Z + 2;
                    else cp.Z = (flight) ? (head.Z) * 8 + 13 + 2 + 2 * ((int)(user[0, 0, 1].Z - head.Z * 8) / 2) : cp.Z + 2;
                    flight = false;
                    accelerate = 0;
                }
                else
                {
                    if (!flight)
                    {
                        for (int a = 0; a < 4; a++)
                        {
                            for (int b = 0; b < 4; b++)
                            {
                                if ((a != 0 & b != 0) | (a != 0 & b != 3) | (a != 3 & b != 0) | (a != 3 & b != 3))
                                {
                                    uXus = uX;
                                    uYus = uY;
                                    head.X = (int)(user[a, b, 0].X / 8);
                                    if (head.X > 63) { head.X -= 64; uXus += 1; }
                                    else if (head.X < 0) { head.X += 64; uXus -= 1; }
                                    head.Y = (int)(user[a, b, 0].Y / 8);
                                    if (head.Y > 63) { head.Y -= 64; uYus += 1; }
                                    else if (head.Y < 0) { head.Y += 64; uYus -= 1; }
                                    head.Z = (int)(user[a, b, 0].Z / 8);

                                    int sbx = (int)(user[a, b, 0].X - head.X * 8) / 2;
                                    int sby = (int)(user[a, b, 0].Y - head.Y * 8) / 2;
                                    int sbz = (int)(user[a, b, 0].Z - head.Z * 8) / 2;
                                    if (!m[uXus, uYus].Map[(int)head.X, (int)head.Y, (int)head.Z].sb)
                                    {
                                        if (objectlist[m[uXus, uYus].Map[(int)head.X, (int)head.Y, (int)head.Z].type].type == "block")
                                        {
                                            stopp = true;
                                            a = 5;
                                            b = 5;
                                        }
                                    }
                                    else if (m[uXus, uYus].Map[(int)head.X, (int)head.Y, (int)head.Z].sb)
                                    {
                                        if (objectlist[m[uXus, uYus].Map[(int)head.X, (int)head.Y, (int)head.Z].sbtype[sbx, sby, sbz]].type == "block")
                                        {
                                            stopp = true;
                                            a = 5;
                                            b = 5;
                                        }
                                    }
                                }
                            }
                        }
                        if (!stopp)
                        {
                            flight = true;
                        }
                    }
                }
                if (flight)
                {
                    stopp = false;
                    for (int a = 0; a < 4; a++)
                    {
                        for (int b = 0; b < 4; b++)
                        {
                            if ((a != 0 & b != 0) | (a != 0 & b != 3) | (a != 3 & b != 0) | (a != 3 & b != 3))
                            {
                                uXus = uX;
                                uYus = uY;
                                head.X = (int)(user[a, b, 8].X / 8);
                                if (head.X > 63) { head.X -= 64; uXus += 1; }
                                else if (head.X < 0) { head.X += 64; uXus -= 1; }
                                head.Y = (int)(user[a, b, 8].Y / 8);
                                if (head.Y > 63) { head.Y -= 64; uYus += 1; }
                                else if (head.Y < 0) { head.Y += 64; uYus -= 1; }
                                head.Z = (int)(user[a, b, 8].Z / 8);

                                int sbx = (int)(user[a, b, 8].X - head.X * 8) / 2;
                                int sby = (int)(user[a, b, 8].Y - head.Y * 8) / 2;
                                int sbz = (int)(user[a, b, 8].Z - head.Z * 8) / 2;
                                if (!m[uXus, uYus].Map[(int)head.X, (int)head.Y, (int)head.Z].sb)
                                {
                                    if (objectlist[m[uXus, uYus].Map[(int)head.X, (int)head.Y, (int)head.Z].type].type == "block")
                                    {
                                        stopp = true;
                                        a = 5;
                                        b = 5;
                                    }
                                }
                                else if (m[uXus, uYus].Map[(int)head.X, (int)head.Y, (int)head.Z].sb)
                                {
                                    if (objectlist[m[uXus, uYus].Map[(int)head.X, (int)head.Y, (int)head.Z].sbtype[sbx, sby, sbz]].type == "block")
                                    {
                                        stopp = true;
                                        a = 5;
                                        b = 5;
                                    }
                                }
                            }
                        }
                    }
                    if (stopp)
                    {
                        cp.Z = (int)(cp.Z / 8) * 8 - 1;
                        accelerate = 0;
                    }
                }

                ct = cp + cn;

                float x = ct.X / 8;
                float y = ct.Y / 8;
                float z = ct.Z / 8, z0 = z;
                int n = 0;
                pX = uX;
                pY = uY;

                if (x >= 64) { pX++; x -= 64; }
                else if (x < 0) { pX--; x += 64; }
                y += (ct.Y - cp.Y) / 8;
                if (y >= 64) { pY++; y -= 64; }
                else if (y < 0) { pY--; y += 64; }

                float x0 = x;
                float y0 = y;

                while (m[pX, pY].Map[(int)x, (int)y, (int)z].type == 0 & n < 1000)
                {
                    p0X = pX;
                    p0Y = pY;
                    x0 = x;
                    y0 = y;
                    z0 = z;
                    x += (ct.X - cp.X) / 320;
                    if (x >= 64) { pX++; x -= 64;}
                    else if (x < 0) { pX--; x += 64;}
                    y += (ct.Y - cp.Y) / 320;
                    if (y >= 64) { pY++; y -= 64;}
                    else if (y < 0) { pY--; y += 64;}
                    z += (ct.Z - cp.Z) / 320;
                    n++;
                }

                pos = new Vector3(x, y, z);

                pos0 = new Vector3((int)x0, (int)y0, (int)z0);
            }

            //   try
            {
                dev.BeginScene();
                dev.RenderState.CullMode = Cull.None;
                dev.RenderState.ZBufferEnable = true;
                dev.RenderState.NormalizeNormals = true;
                dev.RenderState.Lighting = true;
                dev.RenderState.FillMode = FillMode.Solid;

                dev.RenderState.AlphaBlendEnable = false;//прозрачностьToolStripMenuItem.Checked;
                dev.RenderState.AlphaTestEnable = false;//прозрачностьToolStripMenuItem.Checked;
                dev.RenderState.ReferenceAlpha = 15;
                dev.RenderState.AlphaFunction = Compare.Greater;
                dev.RenderState.SourceBlend = Blend.SourceAlpha;
                dev.RenderState.DestinationBlend = Blend.InvSourceAlpha;

                dev.Lights[0].Direction = sun;
                dev.Lights[0].Enabled = true;

                dev.Clear(ClearFlags.ZBuffer | ClearFlags.Target, Color.FromArgb((int)sune - 10, (int)sune - 10, (int)sune-10), 1f, 0);
                dev.Transform.View = Matrix.LookAtRH(cp/* *200-ct*199*/, ct, cup);

                float ar = (float)this.ClientSize.Width / this.ClientSize.Height;

                dev.Transform.Projection = Matrix.PerspectiveFovRH(pi / 4, ar, 1, visible * 11 * 5);

                dev.VertexFormat = VertexFormats.PositionNormal | VertexFormats.Texture1;

                dev.SamplerState[0].MagFilter = TextureFilter.None;
                dev.SamplerState[0].MinFilter = TextureFilter.None;
                dev.SamplerState[0].MipFilter = TextureFilter.None;
                dev.SetStreamSource(0, vbplane, 0);
                dev.Indices = ibplane;
                sbyte x0;
                sbyte x1;
                sbyte y0;
                sbyte y1;
                sbyte z0;
                sbyte z1;
                byte coi = 3;

                /*************ЭКСПЕРИМЕНТАЛЬНО!!!!*****************/

                int numsetb = 0;
                int numdrawb = 0;
                int numsetsb = 0;
                double mpx = Math.Pow(ct.X - cp.X, coi);
                double mpy = Math.Pow(ct.Y - cp.Y, coi);
                double mpz = Math.Pow(ct.Z - cp.Z, coi);

                x0 = (sbyte)(cp.X / 8 + visible * (mpx - 1));
                x1 = (sbyte)(cp.X / 8 + visible * (mpx + 1));

                for (sbyte x = x0; x <= x1; x++)
                {
                    int coeffx = 0;
                    sbyte xm = x;
                    byte pXm = (byte)uX;
                    if (xm < 0)
                    {
                        xm += 64;
                        pXm--;
                        coeffx -= 512;
                    }
                    else if (xm >= 64)
                    {
                        xm -= 64;
                        pXm++;
                        coeffx += 512;
                    }
                    float cox = ((cp.X / 8 - x0) * (x1 - x) + (x1 - cp.X / 8) * (x - x0)) / (2.5f * visible);
                    y0 = (sbyte)(cp.Y / 8 + cox * (mpy - 1));

                    y1 = (sbyte)(cp.Y / 8 + cox * (mpy + 1));

                    for (sbyte y = y0; y < y1; y++)
                    {
                        int coeffy = 0;
                        sbyte ym = y;
                        byte pYm = (byte)uY;
                        if (ym < 0)
                        {
                            ym += 64;
                            pYm--;
                            coeffy -= 512;
                        }

                        else if (ym >= 64)
                        {
                            ym -= 64;
                            pYm++;
                            coeffy += 512;
                        }
                        z0 = (sbyte)(cp.Z / 8 + cox * (mpz - 1));
                        z0 = (z0 < 0) ? (sbyte)0 : z0;
                        z1 = (sbyte)(cp.Z / 8 + cox * (mpz + 1));
                        z1 = (z1 >= 64) ? (sbyte)63 : z1;
                        for (sbyte z = z0; z < z1; z++)
                        {

                            numsetb++;

                            if (m[pXm, pYm].Map[xm, ym, z].bvis == true)
                            {
                                if (!m[pXm, pYm].Map[xm, ym, z].sb)//если блок большой
                                {
                                    dev.Transform.World = Matrix.Translation(boxcoord[xm, ym, z].X + coeffx, boxcoord[xm, ym, z].Y + coeffy, boxcoord[xm, ym, z].Z);
                                    dev.SetStreamSource(0, vbplane, 0);
                                    dev.Indices = ibplane;

                                    for (int val = 0; val < 6; val++)
                                    {
                                        if (m[pXm, pYm].Map[xm, ym, z].vis[val])
                                        {
                                            byte colorcor = (byte)(sune * (m[pXm, pYm].Map[xm, ym, z].taches[1, val] + 1) / 21);
                                            mat.Diffuse = Color.FromArgb(colorcor, colorcor, colorcor);
                                            dev.Material = mat;
                                            if (!m[pXm, pYm].Map[xm, ym, z].alt[val]) dev.SetTexture(0, objectlist[m[pXm, pYm].Map[xm, ym, z].type].texworld); else dev.SetTexture(0, objectlist[m[pXm, pYm].Map[xm, ym, z].type].alttexworld);
                                            dev.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 6, val * 6, 2);
                                        }
                                    }
                                    numdrawb++;
                                }
                                else //если блок разделён на маленькие
                                {
                                    try
                                    {
                                        if ((new Vector3(boxcoord[xm, ym, z].X + coeffx, boxcoord[xm, ym, z].Y + coeffy, boxcoord[xm, ym, z].Z) - cp).Length() < 40 * 8 | HighRes_Butt.Checked)
                                        {
                                            for (byte sbX = 0; sbX < 4; sbX++)
                                            {
                                                for (byte sbY = 0; sbY < 4; sbY++)
                                                {
                                                    for (byte sbZ = 0; sbZ < 4; sbZ++)
                                                    {
                                                        numsetsb++;
                                                        if (m[pXm, pYm].Map[xm, ym, z].sbtype[sbX, sbY, sbZ] != 0)
                                                        {
                                                            dev.Transform.World = Matrix.Translation(boxcoord[xm, ym, z].X + coeffx + smallboxcoord[sbX, sbY, sbZ].X, boxcoord[xm, ym, z].Y + coeffy + smallboxcoord[sbX, sbY, sbZ].Y, boxcoord[xm, ym, z].Z + smallboxcoord[sbX, sbY, sbZ].Z);
                                                            dev.SetStreamSource(0, vbplane, 0);
                                                            dev.Indices = ibplane;
                                                            for (byte val = 0; val < 6; val++)
                                                            {
                                                                if (m[pXm, pYm].Map[xm, ym, z].sbvis[sbX, sbY, sbZ, val])
                                                                {
                                                                    byte colorcor = (byte)(sune * (m[pXm, pYm].Map[xm, ym, z].sbtach[sbX, sbY, sbZ, val, 1] + 1) / 21);
                                                                    mat.Diffuse = Color.FromArgb(colorcor, colorcor, colorcor);
                                                                    dev.Material = mat;
                                                                    if (!m[pXm, pYm].Map[xm, ym, z].sbalt[sbX, sbY, sbZ, val]) dev.SetTexture(0, objectlist[m[pXm, pYm].Map[xm, ym, z].sbtype[sbX, sbY, sbZ]].texworld); else dev.SetTexture(0, objectlist[m[pXm, pYm].Map[xm, ym, z].sbtype[sbX, sbY, sbZ]].alttexworld);
                                                                    dev.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 6, 36 + val * 6, 2);
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            dev.Transform.World = Matrix.Translation(boxcoord[xm, ym, z].X + coeffx, boxcoord[xm, ym, z].Y + coeffy, boxcoord[xm, ym, z].Z);
                                            dev.SetStreamSource(0, vbplane, 0);
                                            dev.Indices = ibplane;

                                            for (int val = 0; val < 6; val++)
                                            {
                                                {
                                                    byte colorcor = (byte)(sune);
                                                    mat.Diffuse = Color.FromArgb(colorcor, colorcor, colorcor);
                                                    dev.Material = mat;
                                                    dev.SetTexture(0, objectlist[m[pXm, pYm].Map[xm, ym, z].type].texworld);
                                                    dev.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 6, val * 6, 2);
                                                }
                                            }
                                            numdrawb++;
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                }
                LIN.Width = 3f;
                sp = new Sprite(dev);
                font0 = new Microsoft.DirectX.Direct3D.Font(dev, fonts);
                if (InventoryOpen) //если инвентарь открыт, рисуем весь инвентарь
                {
                    byte visibleinv = (InventoryOpen) ? (byte)105 : (byte)20;
                    for (byte ui = 0; ui < visibleinv; ui++)
                    {
                        if (ui >= rightsize & ui < 10) ui = 10;
                        if (ui >= leftsize + 10 & ui < 20) ui = 20;
                        if (ui >= backpacksize + 20 & ui < 80) ui = 80;
                        if (ui >= rsize + 80 & ui < 100) ui = 100;
                        // if (ui < rightsize | (ui > 9 & ui < leftsize + 10) | (ui > 19 & ui < 20 + backpacksize) | ui > 79 & ui < 80 + rsize | ui > 99)
                        {
                            if (inventory[ui, 0] != 0)
                            {
                                sp.Begin(SpriteFlags.None);
                                sp.Draw(objectlist[inventory[ui, 0]].texinv, new Rectangle(0, 0, coeffwidth, coeffwidth), new Vector3(coeffwidth, coeffwidth, 0), new Vector3(invcoord[ui].X, invcoord[ui].Y, 0), Color.White.ToArgb());
                                //sp.Draw(objectlist[inventory[ui, 0]].texinv, new Rectangle(0, 0, 30, 30), new Vector3(coeffwidth, coeffwidth, 0), new Vector3(invcoord[ui].X, invcoord[ui].Y, 0), Color.White.ToArgb());
                                sp.End();
                                if (objectlist[inventory[ui, 0]].stacksize > 1) font0.DrawText(null, inventory[ui, 1].ToString(), new Point(invcoord[ui].X - coeffwidth / 2, invcoord[ui].Y - coeffwidth / 2), Color.Black);
                                else
                                {
                                    byte hilled = (byte)(40 * (objectlist[inventory[ui, 0]].standartHill - inventory[ui, 1]) / objectlist[inventory[ui, 0]].standartHill);
                                    Sprite sp2 = new Sprite(dev);
                                    sp2.Begin(SpriteFlags.None);
                                    sp2.Draw2D(toolrepair, new Rectangle(0, 0, 5, 40 - hilled), new Rectangle(0, 0, 5, 40), new Point(0, 0), -pi, new Point(coeffwidth / 2 - invcoord[ui].X - 19, coeffwidth / 2 - invcoord[ui].Y - 20 + hilled * 25), Color.White.ToArgb());
                                    //sp = new Sprite(dev);
                                    // sp.Draw2D(,)
                                    //sp.Draw(toolrepair, new Rectangle(0, 0, 5, 30), new Vector3(coeffwidth, coeffwidth, 0), new Vector3(handinvcoord[ui].X+35, handinvcoord[ui].Y, 0), Color.White.ToArgb());
                                    sp2.End();
                                    sp2.Dispose();
                                }
                            }
                            handinvcol = (ui == selectinv) ? Color.White : Color.Blue;
                            vec1[0] = new Vector2(invcoord[ui].X, invcoord[ui].Y);
                            vec1[1] = new Vector2(invcoord[ui].X, invcoord[ui].Y - coeffwidth);
                            vec1[2] = new Vector2(invcoord[ui].X - coeffwidth, invcoord[ui].Y - coeffwidth);
                            vec1[3] = new Vector2(invcoord[ui].X - coeffwidth, invcoord[ui].Y);
                            vec1[4] = vec1[0];
                            LIN.Draw(vec1, handinvcol);
                        }
                    }
                    if (translateinv[0] != 0)
                    {
                        sp.Begin(SpriteFlags.None);
                        sp.Draw(objectlist[translateinv[0]].texinv, new Rectangle(0, 0, coeffwidth, coeffwidth), new Vector3(coeffwidth, coeffwidth, 0), new Vector3(Cursor.Position.X - Location.X, Cursor.Position.Y - Location.Y, 0), Color.White.ToArgb());
                        sp.End();
                        if (objectlist[translateinv[0]].stacksize > 1) font0.DrawText(null, translateinv[1].ToString(), new Point(Cursor.Position.X - Location.X - coeffwidth / 2, Cursor.Position.Y - Location.Y - coeffwidth / 2), Color.Black);
                    }
                }
                else //если инвентарь закрыт, рисуем пояс
                {
                    Vector3 cnnz = new Vector3(0, 0, 0);
                    cnnz.X = (ct - cp).X;
                    cnnz.Y = (ct - cp).Y;
                    float corr = 1 / cnnz.Length();
                    cnnz.X = cnnz.X * corr;
                    cnnz.Y = cnnz.Y * corr;
                    cnnz.Z = (ct - cp).Z / 1.5f;
                    Vector3 cnnze = new Vector3(cnnz.Y, cnnz.X, 0);
                    Vector3 cc = cnnz;
                    Vector3 cc1 = Vector3.Cross(cup, cc);
                    float[] beat = new float[6];
                    for (int bt = 0; bt < 6; bt++)
                    {
                        beat[bt] = 1 + accelhand[bt];
                    }
                    for (byte ui = 0; ui < rightsize; ui++)
                    {
                        handinvcol = (ui == handRUse) ? Color.White : Color.Blue;
                        if (inventory[ui, 0] != 0)
                        {

                            sp.Begin(SpriteFlags.None);
                            sp.Draw(objectlist[inventory[ui, 0]].texinv, new Rectangle(0, 0, coeffwidth, coeffwidth), new Vector3(coeffwidth, coeffwidth, 0), new Vector3(handinvcoord[ui].X, handinvcoord[ui].Y, 0), Color.White.ToArgb());
                            sp.End();
                            if (objectlist[inventory[ui, 0]].stacksize > 1)
                            { font0.DrawText(null, inventory[ui, 1].ToString(), new Point(handinvcoord[ui].X - coeffwidth / 2, handinvcoord[ui].Y - coeffwidth / 2), Color.Black); }
                            else
                            {
                                byte hilled = (byte)(40 * (objectlist[inventory[ui, 0]].standartHill - inventory[ui, 1]) / objectlist[inventory[ui, 0]].standartHill);
                                Sprite sp2 = new Sprite(dev);
                                sp2.Begin(SpriteFlags.None);
                                sp2.Draw2D(toolrepair, new Rectangle(0, 0, 5, 40 - hilled), new Rectangle(0, 0, 5, 40), new Point(0, 0), -pi, new Point(-handinvcoord[ui].X + 1, -handinvcoord[ui].Y + hilled * 25), Color.White.ToArgb());
                                //sp = new Sprite(dev);
                                // sp.Draw2D(,)
                                //sp.Draw(toolrepair, new Rectangle(0, 0, 5, 30), new Vector3(coeffwidth, coeffwidth, 0), new Vector3(handinvcoord[ui].X+35, handinvcoord[ui].Y, 0), Color.White.ToArgb());
                                sp2.End();
                                sp2.Dispose();
                            }

                            if (ui == handRUse & objectlist[inventory[ui, 0]].type == "block")
                            {
                                font0.DrawText(null, objectlist[inventory[ui, 0]].name, new Point(this.Width - 50, this.Height - 100), Color.Black);
                                // Vector3 cc = ct - cp;
                                // cc.Z = 0;
                                // Vector3 cc1 = Vector3.Cross(cup, cc);
                                Vector3 weap = cp + 3 * cc * beat[0] - 2 * cc1;
                                weap.Z -= 2 * beat[2];
                                mat.Diffuse = Color.White;
                                dev.Material = mat;
                                dev.RenderState.FillMode = FillMode.Solid;
                                dev.SetTexture(0, objectlist[inventory[ui, 0]].texworld);
                                //label5.Text = (-(float)Math.Atan2(cc.X , cc.Y)).ToString();
                                dev.Transform.World = Matrix.RotationZ(-(float)Math.Atan2(cc.X, cc.Y)) * Matrix.Translation(weap);
                                for (byte val = 0; val < 6; val++) dev.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 6, 72 + val * 6, 2);
                                //dev.DrawUserPrimitives(PrimitiveType.TriangleList, numii, dp1);
                            }
                        }
                        vec1[0] = new Vector2(handinvcoord[ui].X, handinvcoord[ui].Y);
                        vec1[1] = new Vector2(handinvcoord[ui].X, handinvcoord[ui].Y - coeffwidth);
                        vec1[2] = new Vector2(handinvcoord[ui].X - coeffwidth, handinvcoord[ui].Y - coeffwidth);
                        vec1[3] = new Vector2(handinvcoord[ui].X - coeffwidth, handinvcoord[ui].Y);
                        vec1[4] = vec1[0];
                        LIN.Draw(vec1, handinvcol);
                    }
                    for (byte ui = 10; ui < 10 + leftsize; ui++)
                    {
                        handinvcol = (ui == handLUse) ? Color.Orange : Color.Blue;
                        if (inventory[ui, 0] != 0)
                        {
                            sp.Begin(SpriteFlags.None);
                            sp.Draw(objectlist[inventory[ui, 0]].texinv, new Rectangle(0, 0, coeffwidth, coeffwidth), new Vector3(coeffwidth, coeffwidth, 0), new Vector3(handinvcoord[ui].X, handinvcoord[ui].Y, 0), Color.White.ToArgb());
                            sp.End();
                            if (objectlist[inventory[ui, 0]].stacksize > 1)
                                font0.DrawText(null, inventory[ui, 1].ToString(), new Point(handinvcoord[ui].X - coeffwidth / 2, handinvcoord[ui].Y - coeffwidth / 2), Color.Black);
                            else
                            {
                                byte hilled = (byte)(40 * (objectlist[inventory[ui, 0]].standartHill - inventory[ui, 1]) / objectlist[inventory[ui, 0]].standartHill);
                                Sprite sp2 = new Sprite(dev);
                                sp2.Begin(SpriteFlags.None);
                                sp2.Draw2D(toolrepair, new Rectangle(0, 0, 5, 40 - hilled), new Rectangle(0, 0, 5, 40), new Point(0, 0), -pi, new Point(coeffwidth / 2 - handinvcoord[ui].X - 19, coeffwidth / 2 - handinvcoord[ui].Y - 20 + hilled * 25), Color.White.ToArgb());
                                //sp = new Sprite(dev);
                                // sp.Draw2D(,)
                                //sp.Draw(toolrepair, new Rectangle(0, 0, 5, 30), new Vector3(coeffwidth, coeffwidth, 0), new Vector3(handinvcoord[ui].X+35, handinvcoord[ui].Y, 0), Color.White.ToArgb());
                                sp2.End();
                                sp2.Dispose();
                            }
                            if (ui == handLUse & objectlist[inventory[ui, 0]].type == "block")
                            {
                                font0.DrawText(null, objectlist[inventory[ui, 0]].name, new Point(50, this.Height - 100), Color.Black);
                                Vector3 weap = cp + 3 * cc * beat[3] + 3f * cc1;
                                weap.Z -= 2 * beat[5];
                                mat.Diffuse = Color.White;
                                dev.Material = mat;
                                dev.RenderState.FillMode = FillMode.Solid;
                                dev.SetTexture(0, objectlist[inventory[ui, 0]].texworld);
                                //label5.Text = (-(float)Math.Atan2(cc.X , cc.Y)).ToString();
                                dev.Transform.World = Matrix.RotationZ(-(float)Math.Atan2(cc.X, cc.Y)) * Matrix.Translation(weap);
                                for (byte val = 0; val < 6; val++) dev.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 6, 72 + val * 6, 2);
                                //dev.DrawUserPrimitives(PrimitiveType.TriangleList, numii, dp1);
                            }
                        }

                        vec1[0] = new Vector2(handinvcoord[ui].X, handinvcoord[ui].Y);
                        vec1[1] = new Vector2(handinvcoord[ui].X, handinvcoord[ui].Y - coeffwidth);
                        vec1[2] = new Vector2(handinvcoord[ui].X - coeffwidth, handinvcoord[ui].Y - coeffwidth);
                        vec1[3] = new Vector2(handinvcoord[ui].X - coeffwidth, handinvcoord[ui].Y);
                        vec1[4] = vec1[0];
                        LIN.Draw(vec1, handinvcol);
                    }
                    for (byte bt = 0; bt < 6; bt++)
                    {
                        if (accelhand[bt] < -0.3) accelhand[bt] = 0;
                        if (accelhand[bt] > 0) accelhand[bt] -= 0.05f;
                    }
                    if (crushing & ((accelhand[0] < 0.2 & !LeftHand) | (accelhand[3] < 0.2 & LeftHand)))
                    { docrush(); }
                    if (seting & ((accelhand[2] < 0.2 & !LeftHand) | (accelhand[5] < 0.2 & LeftHand)))
                    { doset(); }
                    LIN.Width = 2f;
                    LIN.Draw(vec, Color.Black);     //отрисовка "Прицела"
                }

                if (droped > 0)    //отображение выпавших предметов
                {
                    if (droped > drop.Length / 5 - 5)
                    {

                    }
                    for (byte dropp = 0; dropp < droped; dropp++)
                    {
                        if (drop[dropp].type == 0 | drop[dropp].col == 0 | drop[dropp].time > 500)
                        {
                            for (byte dr = dropp; dr < droped; dr++)
                            {
                                drop[dropp] = drop[dropp + 1];
                            }
                            droped--;
                        }//удаление хранимых блоков, смещение информации
                        float dropx = drop[dropp].position.X + drop[dropp].acceleration.X;
                        float dropy = drop[dropp].position.Y + drop[dropp].acceleration.Y;
                        float dropz = drop[dropp].position.Z + drop[dropp].acceleration.Z;
                        if (objectlist[m[(int)dropx / 512, (int)drop[dropp].position.Y / 512].Map[(int)(dropx / 8) % 64, (int)(drop[dropp].position.Y / 8) % 64, (int)(drop[dropp].position.Z / 8) % 64].type].type == "block") drop[dropp].acceleration.X = 0;
                        else if (objectlist[m[(int)dropx / 512, (int)drop[dropp].position.Y / 512].Map[(int)(dropx / 8) % 64, (int)(drop[dropp].position.Y / 8) % 64, (int)(drop[dropp].position.Z / 8) % 64].type].type == "liguid block") drop[dropp].acceleration.X = drop[dropp].acceleration.X * 2 / 3;
                        if (objectlist[m[(int)drop[dropp].position.X / 512, (int)dropy / 512].Map[(int)(drop[dropp].position.X / 8) % 64, (int)(dropy / 8) % 64, (int)(drop[dropp].position.Z / 8) % 64].type].type == "block") drop[dropp].acceleration.Y = 0;
                        else if (objectlist[m[(int)drop[dropp].position.X / 512, (int)dropy / 512].Map[(int)(drop[dropp].position.X / 8) % 64, (int)(dropy / 8) % 64, (int)(drop[dropp].position.Z / 8) % 64].type].type == "liguid block") drop[dropp].acceleration.Y = drop[dropp].acceleration.Y * 2 / 3;
                        if (objectlist[m[(int)drop[dropp].position.X / 512, (int)drop[dropp].position.Y / 512].Map[(int)(drop[dropp].position.X / 8) % 64, (int)(drop[dropp].position.Y / 8) % 64, (int)(dropz / 8) % 64].type].type == "block")
                        {
                            if (m[(int)drop[dropp].position.X / 512, (int)drop[dropp].position.Y / 512].Map[(int)(drop[dropp].position.X / 8) % 64, (int)(drop[dropp].position.Y / 8) % 64, (int)(dropz / 8) % 64].sb)
                            {
                                if (m[(int)drop[dropp].position.X / 512, (int)drop[dropp].position.Y / 512].Map[(int)(drop[dropp].position.X / 8) % 64, (int)(drop[dropp].position.Y / 8) % 64, (int)(dropz / 8) % 64].sbtype[(int)(drop[dropp].position.X / 2) % 4, (int)(drop[dropp].position.Y / 2) % 4, (int)(dropz / 2) % 4] != 0)
                                { drop[dropp].acceleration.X = 0; drop[dropp].acceleration.Y = 0; drop[dropp].acceleration.Z = 0; drop[dropp].position.Z = (int)(dropz / 8) * 8 + (int)(dropz / 2) % 4 * 2 + 2; }
                                else
                                {
                                    drop[dropp].acceleration.Z -= 0.1f;
                                    //if (m[(int)drop[dropp].position.X / 512, (int)drop[dropp].position.Y / 512].Map[(int)(drop[dropp].position.X / 8) % 64, (int)(drop[dropp].position.Y / 8) % 64, (int)(dropz / 8) % 64].sbtype[(int)(drop[dropp].position.X / 2) % 4, (int)(drop[dropp].position.Y / 2) % 4, (int)(dropz / 2) % 4] != 0)
                                    //{ drop[dropp].acceleration.X = 0; drop[dropp].acceleration.Y = 0; drop[dropp].acceleration.Z = 0; }
                                }
                            }
                            else { drop[dropp].acceleration.X = 0; drop[dropp].acceleration.Y = 0; drop[dropp].acceleration.Z = 0; drop[dropp].position.Z = (int)(dropz / 8) * 8 + 8; }
                        }
                        else
                        {
                            if (objectlist[m[(int)drop[dropp].position.X / 512, (int)drop[dropp].position.Y / 512].Map[(int)(drop[dropp].position.X / 8) % 64, (int)(drop[dropp].position.Y / 8) % 64, (int)(dropz / 8) % 64].type].type == "liguid block")
                                drop[dropp].acceleration.Z -= 0.05f;
                            else drop[dropp].acceleration.Z -= 0.1f;
                            //if (m[(int)drop[dropp].position.X / 512, (int)drop[dropp].position.Y / 512].Map[(int)(drop[dropp].position.X / 8) % 64, (int)(drop[dropp].position.Y / 8) % 64, (int)((drop[dropp].position.Z + drop[dropp].acceleration.Z) / 8) % 64].type != 0)
                            //{ drop[dropp].acceleration.Z = 0;}
                        }
                        drop[dropp].position += drop[dropp].acceleration;
                        drop[dropp].time++;
                        //if (drop[dropp].time > 250 & rand.Next(15) != 0)
                        //drop[dropp].time = 0;
                        if ((new Vector3(cp.X + uX * 512, cp.Y + uY * 512, cp.Z) - drop[dropp].position).Length() <= visible * 10)
                        {
                            dev.Transform.World = Matrix.Translation(new Vector3(-0.5f, -0.5f, 0)) * Matrix.RotationZ(drop[dropp].time % 62.8f / 10) * Matrix.Translation(new Vector3(drop[dropp].position.X % 512, drop[dropp].position.Y % 512, drop[dropp].position.Z % 512));
                            for (byte val = 0; val < 6; val++)
                            {
                                //byte colorcor = (byte)(sune * (m[(int)drop[dropp].position.X / 64, (int)drop[dropp].position.Y / 64].Map[(int)drop[dropp].position.X % 64, (int)drop[dropp].position.Y % 64, (int)drop[dropp].position.Z % 64].tach[sbX, sbY, sbZ, val, 0] + 1) / 21);
                                // mat.Diffuse = Color.FromArgb(colorcor, colorcor, colorcor);
                                mat.Diffuse = Color.White;
                                dev.Material = mat;
                                dev.SetTexture(0, objectlist[drop[dropp].type].texworld);
                                //dev.DrawUserPrimitives(PrimitiveType.TriangleList, numii, dp);
                                dev.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 6, 72 + val * 6, 2);
                            }
                            if ((new Vector3(cp.X + uX * 512, cp.Y + uY * 512, cp.Z - 2) - drop[dropp].position).Length() < (ct - cp).Length() * 20)
                            {
                                drop[dropp].acceleration += (new Vector3(cp.X + uX * 512, cp.Y + uY * 512, 0) - new Vector3(drop[dropp].position.X, drop[dropp].position.Y, 0)) * (1 / (3 * (new Vector3(cp.X + uX * 512, cp.Y + uY * 512, cp.Z) - drop[dropp].position).Length()));
                                if ((new Vector3(cp.X + uX * 512, cp.Y + uY * 512, cp.Z - 2) - drop[dropp].position).Length() < (ct - cp).Length() * 16 & drop[dropp].time > 15)
                                {
                                    sbyte select = -1;
                                    do
                                    {
                                        select++;
                                        if (select >= rightsize & select < 10) select = 10;
                                        if (select >= leftsize + 10 & select < 20) select = 20;
                                        if (select >= backpacksize + 20 & select < 80) select = 80;
                                        if (select >= rsize + 80 & select < 100) select = 100;
                                    }
                                    while (inventory[select, 0] != 0 & (inventory[select, 0] != drop[dropp].type | inventory[select, 1] >= objectlist[drop[dropp].type].stacksize) & select < 100);
                                    if (select < 100)
                                    {
                                        byte[] si = new byte[2];
                                        si[0] = drop[dropp].type;
                                        si[1] = (byte)(drop[dropp].col + inventory[select, 1]);
                                        inventory[select, 0] = si[0];
                                        if (si[1] > objectlist[si[0]].stacksize)
                                        {
                                            inventory[select, 1] = objectlist[si[0]].stacksize;
                                            drop[dropp].col = (byte)(si[1] - objectlist[si[0]].stacksize);
                                        }
                                        else
                                        {
                                            inventory[select, 1] = si[1];
                                            drop[dropp].type = 0;
                                            drop[dropp].col = 0;
                                            drop[dropp].time = 0;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (techno)//Отображение технической информации
                {
                    font0.DrawText(null, "fps " + fps.ToString() + " cps " + cps.ToString(), new Point(50, 50), Color.Black);
                    font0.DrawText(null, "Память " + dev.AvailableTextureMemory.ToString(), new Point(50, 65), Color.Black);
                    string s = "Состояние участков: ";
                    s += (startshift) ? "Загрузка" : "В покое";
                    s += (finishshift) ? " / ожидается завершение" : "";
                    font0.DrawText(null, s, new Point(50, 80), Color.Black);
                    font0.DrawText(null, "Обработано блоков: " + numsetb.ToString() + ". Из них отрисовано: " + numdrawb.ToString() + ". Дополнительно отрисовано дробных: " + numsetsb.ToString(), new Point(50, 95), Color.Black);
                    font0.DrawText(null, "Рабочее поле № X " + (uX).ToString() + ", Y " + (uY).ToString() + ". Координаты глаз: X=" + ((int)head.X).ToString() + " Y=" + ((int)head.Y).ToString() + " Z=" + ((int)cp.Z / 8 + 1).ToString(), new Point(50, 110), Color.Black);
                    font0.DrawText(null, "Реальный номер поля: X" + m[uX, uY].X.ToString() + ", Y " + m[uX, uY].Y.ToString(), new Point(50, 125), Color.Black);
                }

                label1.Text = precipnum.ToString() + " / " + wheather.ToString();
                
                
                /*if (rand.Next(1000) == 0)
                    wheather = (byte)rand.Next(4);*/
                wheather = 1;
                if (wheather != 0)
                {
                    switch (wheather)
                    {
                        case 1:
                            precipspeed = new bvec3(0, 0, -1);
                            preciptype = 9;
                            precipintensity = 1;
                            break;
                        case 2:
                            precipspeed = new bvec3(0, 0, -3);
                            preciptype = 8;
                            precipintensity = 1;
                            break;
                        case 3:
                            precipspeed = new bvec3(0, 0, -5);
                            preciptype = 8;
                            precipintensity = 5;
                            break;
                    }
                    for (byte precigenerate = 0; precigenerate < 5; precigenerate++)
                    if (precipnum < 1000)
                    {
                            if (rand.Next(6) > 5 - precipintensity)
                            {
                                prec[precipnum].coordinate = new svec3((short)rand.Next(512), (short)rand.Next(512), 511);
                               // prec[precipnum].coordinate = new svec3((short)100, (short)100, 550);
                                prec[precipnum].speed = precipspeed;
                                prec[precipnum].type = preciptype;
                                precipnum++;
                            }
                    }
                }
                    if (precipnum > 0)
                    {
                        mat.Diffuse = Color.FromArgb((int)sune, (int)sune, (int)sune);
                        
                        for (short precipout = 0; precipout < precipnum; precipout++)
                        {
                            if (new Vector3(cp.X - prec[precipout].coordinate.X, cp.Y - prec[precipout].coordinate.Y, cp.Z - prec[precipout].coordinate.Z).Length() <= visible * 50)
                            {
                                dev.SetTexture(0, objectlist[prec[precipout].type].texworld);
                        dev.Material = mat;
                                dev.Transform.World = Matrix.Translation(new Vector3(prec[precipout].coordinate.X, prec[precipout].coordinate.Y, prec[precipout].coordinate.Z));
                                for (byte val = 0; val < 6; val++)
                                { dev.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 6, 72 + val * 6, 2); }
                            }
                            svec3 precicoordold = prec[precipout].coordinate;
                            prec[precipout].coordinate = new svec3((short)(prec[precipout].coordinate.X + prec[precipout].speed.X), (short)(prec[precipout].coordinate.Y + prec[precipout].speed.Y), (short)(prec[precipout].coordinate.Z + prec[precipout].speed.Z));

                            if (prec[precipout].coordinate.Z > 0 & prec[precipout].coordinate.X > 0 & prec[precipout].coordinate.Y > 0)
                            {
                                strmesh wblock = m[prec[precipout].coordinate.X / 8 / 64, prec[precipout].coordinate.Y / 8 / 64].Map[prec[precipout].coordinate.X / 8 % 64, prec[precipout].coordinate.Y / 8 % 64, prec[precipout].coordinate.Z / 8 % 64];
                                if (wblock.sb)
                                {
                                    if (objectlist[wblock.sbtype[prec[precipout].coordinate.X % 4, prec[precipout].coordinate.Y % 4, prec[precipout].coordinate.Z % 4]].type == "block")
                                    {
                                        if (prec[precipout].type == 9 & rand.Next(10) == 0) addsmallblock((byte)9, (byte)(precicoordold.X / 8 / 64), (byte)(precicoordold.Y / 8 / 64), 0, (byte)(precicoordold.X / 8 % 64), (byte)(precicoordold.Y / 8 % 64), (byte)(precicoordold.Z / 8 % 64), (byte)(precicoordold.X % 4), (byte)(precicoordold.Y % 4), (byte)(precicoordold.Z % 4));
                                        for (short precipshift = precipout; precipshift < precipnum - 1; precipshift++)
                                        {
                                            prec[precipshift] = prec[precipshift + 1];
                                        }
                                        precipnum--;
                                    }
                                }
                                else
                                {
                                    if (objectlist[wblock.type].type == "block")
                                    {
                                        if (prec[precipout].type == 9 & rand.Next(10) == 0) addsmallblock((byte)9, (byte)(precicoordold.X / 8 / 64), (byte)(precicoordold.Y / 8 / 64), 0, (byte)(precicoordold.X / 8 % 64), (byte)(precicoordold.Y / 8 % 64), (byte)(precicoordold.Z / 8 % 64), (byte)(precicoordold.X % 4), (byte)(precicoordold.Y % 4), (byte)(precicoordold.Z % 4));
                                        for (short precipshift = precipout; precipshift < precipnum - 1; precipshift++)
                                        {
                                            prec[precipshift] = prec[precipshift + 1];
                                        }
                                        precipnum--;
                                    }
                                }
                            }
                            else
                            {
                                for (short precipshift = precipout; precipshift < precipnum - 1; precipshift++)
                                {
                                    prec[precipshift] = prec[precipshift + 1];
                                }
                                precipnum--;
                            }
                        }
                    }
                


                sp.Dispose();
                font0.Dispose();

                dev.EndScene();
                dev.Present();
            }
            /*   catch
               {
                   MessageBox.Show("step error");
                   this.Close();
               }*/

            framenum++;
        }

        private void addsmallblock(byte type, byte workX,byte workY,byte workZ,byte workx,byte worky,byte workz, byte worksx,byte worksy,byte worksz)
        {
            if (!m[workX,workY].Map[workx, worky,workz].sb)
            {
                if (objectlist[m[workX, workY].Map[workx, worky, workz].type].type == "Gas block")
                {
                    m[workX, workY].Map[workx, worky, workz] = new strmesh(true);
                }
            }
            if (m[workX, workY].Map[workx, worky, workz].sb)
            {
                for (byte vis = 0; vis< 6; vis++)
                {
                    m[workX, workY].Map[workx, worky, workz].sbvis[worksx, worksy, worksz, vis] = true;
                    m[workX, workY].Map[workx, worky, workz].sbtype[worksx, worksy, worksz] = type;
                }
            }
        }

        private void docrush()
        {
            if (!LeftHand)
            {
                accelhand[0] = 1;
                m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].hill -= 30;
                /*dropinv[droped, 0] = m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].type;
                dropinv[droped, 1] = 64;
                dropinv[droped, 2] = 0;
                dropinvcoord[droped, 0] = new Vector3(pX * 512, pY * 512, 0) + new Vector3((int)pos.X * 8 + 4, (int)pos.Y * 8 + 4, (int)pos.Z * 8 + 4);
                dropinvcoord[droped, 1] = new Vector3(0, 0, 1);
                droped++;
                m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z] = new strmesh(false);*/
                for (sbyte sh = 6; sh >= 0; sh--)
                {
                    sbyte workX = pX;
                    sbyte workY = pY;
                    sbyte workx = (sbyte)(pos.X + shifting[sh].X);
                    if (workx > 63) { if (workX < 3) { workx -= 64; workX++; } else workx = 63; } else if (workx < 0) { if (workX > 0) { workx += 64; workX--; } else workx = 0; }
                    sbyte worky = (sbyte)(pos.Y + shifting[sh].Y);
                    if (worky > 63) { if (workY < 3) { worky -= 64; workY++; } else worky = 63; } else if (worky < 0) { if (workY > 0) { worky += 64; workY--; } else worky = 0; }
                    short workz = (short)(pos.Z + shifting[sh].Z);
                    if (workz >= 64) { workz = 63; } else if (workz < 0) { workz = 0; }
                    m[workX, workY].Map[workx, worky, workz] = checkblock((byte)workX, (byte)workY, (byte)workx, (byte)worky, (byte)workz, false);
                }
                // checkvisiblearoundblock((int)pos.X, (int)pos.Y, (int)pos.Z, pX, pY);
            }
            else
            {
                Vector3 cn = ct - cp;
                int sbx = (int)((pos.X - (int)pos.X) * 4);
                int sby = (int)((pos.Y - (int)pos.Y) * 4);
                int sbz = (int)((pos.Z - (int)pos.Z) * 4);
                accelhand[3] = 1;
            startcrush:
                if (!m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].sb)
                {

                    m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].sbtype = new byte[4, 4, 4];
                    m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].sbvis = new bool[4, 4, 4, 6];
                    m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].sbalt = new bool[4, 4, 4, 6];
                    m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].sbtach = new byte[4, 4, 4, 6, 3];
                    //m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].sbtach = new byte[4, 4, 4, 6];
                    for (byte ex = 0; ex < 4; ex++)
                    {
                        for (byte ey = 0; ey < 4; ey++)
                        {
                            for (byte ez = 0; ez < 4; ez++)
                            {
                                m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].sbtype[ex, ey, ez] = m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].type;
                                //if (m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].type == 5)
                                {
                                    if (ez == 3) m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].sbalt[ex, ey, ez, 0] = m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].alt[0];
                                    if (ez == 0) m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].sbalt[ex, ey, ez, 1] = m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].alt[1];
                                }
                            }
                        }
                    }
                    //m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].taches = new byte[2,6];
                    m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].taches = null;
                    m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].sb = true;
                    //checkvisiblearoundblock((int)pos.X, (int)pos.Y, (int)pos.Z, pX, pY);
                    /*  for (sbyte sh = 6; sh >= 0; sh--)
                      {
                          sbyte workX = pX;
                          sbyte workY = pY;
                          sbyte workx = (sbyte)((short)pos.X + shifting[sh].X);
                          if (workx > 63) { if (workX < 3) { workx -= 64; workX++; } else workx = 63; } else if (workx < 0) { if (workX > 0) { workx += 64; workX--; } else workx = 0; }
                          sbyte worky = (sbyte)((short)pos.Y + shifting[sh].Y);
                          if (worky > 63) { if (workY < 3) { worky -= 64; workY++; } else worky = 63; } else if (worky < 0) { if (workY > 0) { worky += 64; workY--; } else worky = 0; }
                          short workz = (short)((short)pos.Z + shifting[sh].Z);
                          if (workz >= 64) { workz = 63; } else if (workz < 0) { workz = 0; }
                          //m[workX, workY].Map[workx, worky, workz] = checktatches(m, workX, workY, workx, worky, workz);
                      }*/
                    m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].vis = null;
                    m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].alt = null;
                    //m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].taches = null;
                }

                {
                    while (m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].sbtype[sbx, sby, sbz] == 0 & sbx < 4 & sby < 4 & sbz < 4 & sbx >= 0 & sby >= 0 & sbz >= 0 & (pos * 8 - cp).Length() < 40)
                    {
                        pos.X += cn.X / 320;
                        pos.Y += cn.Y / 320;
                        pos.Z += cn.Z / 320;
                        sbx = (int)((pos.X - (int)pos.X) * 4);
                        sby = (int)((pos.Y - (int)pos.Y) * 4);
                        sbz = (int)((pos.Z - (int)pos.Z) * 4);
                        if (!m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].sb) goto startcrush;
                    }
                    drop[droped].type = m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].sbtype[sbx, sby, sbz];
                    drop[droped].col = 1;
                    drop[droped].time = 0;
                    drop[droped].position = new Vector3(pX * 512, pY * 512, 0) + new Vector3((int)pos.X * 8 + sbx * 2 + 1, (int)pos.Y * 8 + sby * 2 + 1, (int)pos.Z * 8 + sbz * 2 + 1);
                    drop[droped].acceleration = new Vector3(0, 0, 1);
                    droped++;
                    m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].sbtype[sbx, sby, sbz] = 0;

                    bool stop = false;
                    for (byte ex = 0; ex < 4; ex++)
                    {
                        for (byte ey = 0; ey < 4; ey++)
                        {
                            for (byte ez = 0; ez < 4; ez++)
                            {
                                if (m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].sbtype[ex, ey, ez] != 0)
                                {
                                    stop = true;
                                    ex = 5;
                                    ey = 5;
                                    ez = 5;
                                }
                            }
                        }
                    }
                    if (!stop)
                    {
                        m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z] = new strmesh(false);
                        /*m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].type = 0;
                        m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].bvis = false;
                        m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].sb = false;*/
                    }
                    else
                    {

                        // m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z] = checkvisiblesb(m, pX, pY, (int)pos.X, (int)pos.Y, (int)pos.Z);
                        //m[pX, pY].Map[(int)pos.X, (int)pos.Y, (int)pos.Z].bvis = checkvisible(m, pX, pY, (int)pos.X, (int)pos.Y, (int)pos.Z);
                    }
                    //checkvisiblearoundblock((int)pos.X, (int)pos.Y, (int)pos.Z, pX, pY);
                    for (byte c = 0; c < 2; c++)
                        for (byte sh = 0; sh < 7; sh++)
                        {
                            sbyte workX = pX;
                            sbyte workY = pY;
                            sbyte workx = (sbyte)(pos.X + shifting[sh].X);
                            if (workx > 63) { if (workX < 3) { workx -= 64; workX++; } else workx = 63; } else if (workx < 0) { if (workX > 0) { workx += 64; workX--; } else workx = 0; }
                            sbyte worky = (sbyte)(pos.Y + shifting[sh].Y);
                            if (worky > 63) { if (workY < 3) { worky -= 64; workY++; } else worky = 63; } else if (worky < 0) { if (workY > 0) { worky += 64; workY--; } else worky = 0; }
                            short workz = (short)(pos.Z + shifting[sh].Z);
                            if (workz >= 64) { workz = 63; } else if (workz < 0) { workz = 0; }
                            m[workX, workY].Map[workx, worky, workz] = checkblock((byte)workX, (byte)workY, (byte)workx, (byte)worky, (byte)workz, false);
                        }
                }

            }
        }
        private void doset()
        {
            sbyte inv = -1;
            //int doo = 0;//ничего
            if ((!LeftHand | (inventory[handLUse, 0] == 0 & LeftHand)) & inventory[handRUse, 0] != 0)
            {
                inv = handRUse;
                accelhand[2] = 1;
            }

            if ((inventory[handRUse, 0] == 0 | LeftHand) & inventory[handLUse, 0] != 0)
            {
                inv = handLUse;
                accelhand[5] = 1;
            }
            else { }
            if (pos0 != new Vector3((int)pos.X, (int)pos.Y, (int)pos.Z) & pos0 != new Vector3((int)cp.X / 8, (int)cp.Y / 8, (int)cp.Z / 8) & pos0 != new Vector3((int)cp.X, (int)cp.Y, (int)cp.Z - 1))
            {
                if (inv != -1)
                {
                    if (objectlist[inventory[inv, 0]].type.Contains( "block"))
                    {
                        addblock(inventory[inv, 0], (sbyte)pos0.X, (sbyte)pos0.Y, (short)pos0.Z, p0X, p0Y);
                        inventory[inv, 1]--;
                        if (inventory[inv, 1] == 0) inventory[inv, 0] = 0;
                    }

                }
                else
                {
                    /* strmesh[, ,] tree0 = tree();

                     for (byte sx = 0; sx < 5; sx++)
                     {
                         for (byte sy = 0; sy < 5; sy++)
                         {
                             for (byte sz = 0; sz < 6; sz++)
                             {
                                 m[p0X, p0Y].Map[(sbyte)pos0.X + sx, (sbyte)pos0.Y + sy, (sbyte)pos0.Z + sz] = tree0[sx, sy, sz];
                             }
                         }
                     }*/
                }
            }
        }
        private void выходToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try { t0.Abort(); }
            catch { }
            this.Close();
        }


        private void Form1_MouseMove(object sender, MouseEventArgs e)
        {
            if (going & !InventoryOpen)
            {
                Vector3 cn = ct - cp;
                if (Cursor.Position == center()) return;

                float dcx = this.ClientSize.Width / 2 - e.X;
                float dcy = this.ClientSize.Height / 2 - e.Y;
                float alpha = dcx / this.ClientSize.Width * pi / 2;
                float beta = dcy / this.ClientSize.Height * pi / 2;

                if ((float)Math.Asin((double)cn.Z) + beta > 1.5) beta = 1.5f - (float)Math.Asin((double)cn.Z);
                if ((float)Math.Asin((double)cn.Z) + beta < -1.5) beta = -1.5f - (float)Math.Asin((double)cn.Z);
                cn = Vector3.TransformCoordinate(cn, Matrix.RotationZ(alpha));
                Vector3 ab = Vector3.Cross(cn, cup);
                cn = Vector3.TransformCoordinate(cn, Matrix.RotationAxis(ab, beta));

                ct = cp + cn;
                Cursor.Position = center();
            }
            if (InventoryOpen)
            {
                int coordX = Cursor.Position.X - Location.X;
                int coordY = Cursor.Position.Y - Location.Y;
                if ((coordX > this.Width / 2 - 1.75 * (coeffwidth + 3)) & (coordY > this.Height / 2 - 4.2 * (coeffwidth + 3)) & (coordX < this.Width / 2 + 2.5 * (coeffwidth + 3)) & (coordY < this.Height / 2 + 5.85 * (coeffwidth + 3)))
                {
                    selectinv = (short)((short)((coordX + 1.75 * (coeffwidth + 3) - this.Width / 2) / (coeffwidth + 3)) * 10 + (coordY + 4 * (coeffwidth + 3) - this.Height / 2) / (coeffwidth + 3) + 20);

                }
                else
                {
                    if ((coordX > this.Width / 2 + 1.3 * (coeffwidth + 3)) & (coordY > this.Height / 2 + 6.8 * (coeffwidth + 3)) & (coordX < this.Width / 2 + 11.25 * (coeffwidth + 3)) & (coordY < this.Height / 2 + 7.8 * (coeffwidth + 3)))
                    {
                        selectinv = (short)(((coordX - 1.3 * (coeffwidth + 3) - this.Width / 2) / (coeffwidth + 3)));
                    }
                    else
                    {
                        if ((coordX > this.Width / 2 - 10.625 * (coeffwidth + 3)) & (coordY > this.Height / 2 + 6.8 * (coeffwidth + 3)) & (coordX < this.Width / 2 - 0.7 * (coeffwidth + 3)) & (coordY < this.Height / 2 + 7.8 * (coeffwidth + 3)))
                        {
                            selectinv = (short)(10 + ((-coordX - 0.7 * (coeffwidth + 3) + this.Width / 2) / (coeffwidth + 3)));
                        }
                        else
                        {
                            if ((coordX > this.Width / 2 - 6.8 * (coeffwidth + 3)) & (coordY > this.Height / 2 - 2.5 * (coeffwidth + 3)) & (coordX < this.Width / 2 - 5.8 * (coeffwidth + 3)) & (coordY < this.Height / 2 + 2.5 * (coeffwidth + 3)))
                            {
                                selectinv = (short)(100 + ((coordY - this.Height / 2 + 2.5 * (coeffwidth + 3)) / (coeffwidth + 3)));
                            }
                            else selectinv = -1;
                        }
                    }
                }
                if ((selectinv > rightsize - 1 & selectinv < 10) | (selectinv > leftsize + 9 & selectinv < 20)) selectinv = -1;
            }
        }

        private Point center()
        {
            Point c = new Point(this.Location.X + this.Width / 2, this.Location.Y + this.Height - this.ClientSize.Height / 2 - (this.Width - this.ClientSize.Width) / 2);
            return c;
        }

        private void Form1_MouseDown(object sender, MouseEventArgs e)
        {
            if (going)
            {
                if (InventoryOpen)//если инвентарь открыт
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        if (selectinv != -1) //если курсор указывает на клетку инвентаря
                        {
                            if (selectinv < 100 | (translateinv[0] == 0) | (selectinv == 102 & objectlist[translateinv[0]].type == "p") | (selectinv == 101 & objectlist[translateinv[0]].type == "topclothe"))
                            {
                                byte[] si = new byte[2];
                                si[0] = translateinv[0];

                                if (translateinv[0] != inventory[selectinv, 0])
                                {
                                    si[1] = translateinv[1];
                                    translateinv[0] = inventory[selectinv, 0];
                                    translateinv[1] = inventory[selectinv, 1];
                                    inventory[selectinv, 0] = si[0];
                                    inventory[selectinv, 1] = si[1];
                                }
                                else
                                {
                                    si[0] = translateinv[0];
                                    si[1] = (byte)(translateinv[1] + inventory[selectinv, 1]);
                                    if (si[1] > objectlist[si[0]].stacksize)
                                    {
                                        inventory[selectinv, 1] = objectlist[si[0]].stacksize;
                                        translateinv[1] = (byte)(si[1] - objectlist[si[0]].stacksize);
                                    }
                                    else
                                    {
                                        inventory[selectinv, 1] = si[1];
                                        translateinv[0] = 0;
                                        translateinv[1] = 0;
                                    }
                                }
                                setinvcoord();
                            }
                        }
                        else
                        {
                            drop[droped].type = translateinv[0];
                            drop[droped].col = translateinv[1];
                            drop[droped].time = 0;
                            drop[droped].position = new Vector3(uX*512,uY*512,0)+cp+5*(ct-cp);
                            drop[droped].acceleration = (ct-cp)*3;
                            droped++;
                            translateinv[0] = 0;
                            translateinv[1] = 0;
                        }
                    }
                    else
                    {
                        if (e.Button == MouseButtons.Right)
                        {
                            if (selectinv != -1) //если курсор указывает на клетку инвентаря
                            {
                                if (translateinv[0] == inventory[selectinv, 0] | inventory[selectinv, 0] == 0)
                                {
                                    inventory[selectinv, 0] = translateinv[0];
                                    if (inventory[selectinv, 1] < objectlist[inventory[selectinv, 0]].stacksize)
                                    {
                                        inventory[selectinv, 1]++;
                                        translateinv[1]--;
                                        if (translateinv[1] <= 0) translateinv[0] = 0;
                                    }
                                }
                            }
                            else
                            {
                                drop[droped].type = translateinv[0];
                                drop[droped].col = 1;
                                drop[droped].time = 0;
                                drop[droped].position = new Vector3(uX * 512, uY * 512, 0) + cp + 5 * (ct - cp);
                                drop[droped].acceleration = (ct - cp) * 3;
                                droped++;
                                translateinv[1]--;
                                if (translateinv[1] <= 0) translateinv[0] = 0;
                            }
                        }
                    }
                }
                if (!InventoryOpen)//если инвентарь закрыт
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        crushing = true;
                        seting = false;
                    }

                    if (e.Button == MouseButtons.Right)
                    {
                        crushing = false; 
                        seting = true;
                    }
                }




                /*
                                if (e.Button == MouseButtons.Right & pos0 != pos & pos0 != head & pos0 != head)
                                {
                                    int x = (int)pos0.X;
                                    int y = (int)pos0.Y;
                                    int z = (int)pos0.Z;
                                    m[1, 1].Map[x, y, z].type = 4;
                                    m[1, 1].Map[x, y, z].visible = true;
                                    m[1, 1].Map[x, y, z].texture = tex[m[1, 1].Map[x, y, z].type];
                                    m[1, 1].Map[x, y, z].pos = new Vector3(x * 8, y * 8, z * 8);

                                    x -= 1;
                                    if (m[1, 1].Map[x - 1, y, z].type != 0 & m[1, 1].Map[x, y - 1, z].type != 0 & m[1, 1].Map[x, y, z - 1].type != 0 & m[1, 1].Map[x + 1, y, z].type != 0 & m[1, 1].Map[x, y + 1, z].type != 0 & m[1, 1].Map[x, y, z + 1].type != 0)
                                    {
                                        m[1, 1].Map[x, y, z].visible = false;
                                    }
                                    x += 2;
                                    if (m[1, 1].Map[x - 1, y, z].type != 0 & m[1, 1].Map[x, y - 1, z].type != 0 & m[1, 1].Map[x, y, z - 1].type != 0 & m[1, 1].Map[x + 1, y, z].type != 0 & m[1, 1].Map[x, y + 1, z].type != 0 & m[1, 1].Map[x, y, z + 1].type != 0)
                                    {
                                        m[1, 1].Map[x, y, z].visible = false;
                                    }

                                    x -= 1;
                                    y -= 1;
                                    if (m[1, 1].Map[x - 1, y, z].type != 0 & m[1, 1].Map[x, y - 1, z].type != 0 & m[1, 1].Map[x, y, z - 1].type != 0 & m[1, 1].Map[x + 1, y, z].type != 0 & m[1, 1].Map[x, y + 1, z].type != 0 & m[1, 1].Map[x, y, z + 1].type != 0)
                                    {
                                        m[1, 1].Map[x, y, z].visible = false;
                                    }
                                    y += 2;
                                    if (m[1, 1].Map[x - 1, y, z].type != 0 & m[1, 1].Map[x, y - 1, z].type != 0 & m[1, 1].Map[x, y, z - 1].type != 0 & m[1, 1].Map[x + 1, y, z].type != 0 & m[1, 1].Map[x, y + 1, z].type != 0 & m[1, 1].Map[x, y, z + 1].type != 0)
                                    {
                                        m[1, 1].Map[x, y, z].visible = false;
                                    }
                                    y -= 1;
                                    z -= 1;
                                    if (m[1, 1].Map[x - 1, y, z].type != 0 & m[1, 1].Map[x, y - 1, z].type != 0 & m[1, 1].Map[x, y, z - 1].type != 0 & m[1, 1].Map[x + 1, y, z].type != 0 & m[1, 1].Map[x, y + 1, z].type != 0 & m[1, 1].Map[x, y, z + 1].type != 0)
                                    {
                                        m[1, 1].Map[x, y, z].visible = false;
                                    }
                                    z += 2;
                                    if (m[1, 1].Map[x - 1, y, z].type != 0 & m[1, 1].Map[x, y - 1, z].type != 0 & m[1, 1].Map[x, y, z - 1].type != 0 & m[1, 1].Map[x + 1, y, z].type != 0 & m[1, 1].Map[x, y + 1, z].type != 0 & m[1, 1].Map[x, y, z + 1].type != 0)
                                    {
                                        m[1, 1].Map[x, y, z].visible = false;
                                    }
                                }*/
            }
        }

        private void Form1_MouseUp(object sender, MouseEventArgs e)
        {
            if (!InventoryOpen)//если инвентарь закрыт
            {
               crushing = false;
               seting = false;
            }
        }

        public void addblock(byte type, sbyte x, sbyte y, short z, sbyte pX, sbyte pY)
        {
            m[pX, pY].Map[x, y, z] = new strmesh(false);
            m[pX, pY].Map[x, y, z].type = type;
            m[pX, pY].Map[x, y, z].bvis = true;
            m[pX, pY].Map[x, y, z].hill = objectlist[type].standartHill;
            //m[pX, pY].Map[x, y, z].alt = new bool[6];
            //m[pX, pY].Map[x, y, z].pos = new Vector3(x * 8, y * 8, z * 8);
            //checkvisiblearoundblock(x, y, z, pX, pY);
            if (objectlist[m[pX, pY].Map[x, y, z].type].type=="liguid block")
            {
                for (byte sh = 1; sh < 6; sh++)
                { 
                    m[pX, pY].Map[x, y, z].taches[2,sh]=50;
                }
                m[pX, pY].Map[x, y, z].taches[2, 0] = 0;
            }
            for (byte sh = 0; sh < 7; sh++)
            {
                sbyte workX = pX;
                sbyte workY = pY;
                sbyte workx = (sbyte)(x + shifting[sh].X);
                if (workx > 63) { if (workX < 3) { workx -= 64; workX++; } else workx = 63; } else if (workx < 0) { if (workX > 0) { workx += 64; workX--; } else workx = 0; }
                sbyte worky = (sbyte)(y + shifting[sh].Y);
                if (worky > 63) { if (workY < 3) { worky -= 64; workY++; } else worky = 63; } else if (worky < 0) { if (workY > 0) { worky += 64; workY--; } else worky = 0; }
                short workz = (short)(z + shifting[sh].Z);
                if (workz >= 64) { workz = 63; } else if (workz < 0) { workz = 0; }
                m[workX, workY].Map[workx, worky, workz] = checkblock((byte)workX, (byte)workY, (byte)workx, (byte)worky, (byte)workz, false);
            }
        }

    /*    public void checkvisiblearoundblock(int x, int y, int z, int pX, int pY)
        {
            m[pX, pY].Map[x, y, z - 1].bvis = checkvisible(m, pX, pY, x, y, z - 1);
            if (m[pX, pY].Map[x, y, z - 1].sb) m[pX, pY].Map[x, y, z - 1] = checkvisiblesb(m, pX, pY, x, y, z - 1);
            m[pX, pY].Map[x, y, z + 1].bvis = checkvisible(m, pX, pY, x, y, z + 1);
            if (m[pX, pY].Map[x, y, z + 1].sb) m[pX, pY].Map[x, y, z + 1] = checkvisiblesb(m, pX, pY, x, y, z + 1);

            if (x - 1 < 0)
            {
                m[pX - 1, pY].Map[x + 63, y, z].bvis = checkvisible(m, pX - 1, pY, x + 63, y, z);
                if (m[pX - 1, pY].Map[x + 63, y, z].sb) m[pX - 1, pY].Map[x + 63, y, z] = checkvisiblesb(m, pX - 1, pY, x + 63, y, z);
            }
            else
            {
                m[pX, pY].Map[x - 1, y, z].bvis = checkvisible(m, pX, pY, x - 1, y, z);
                if (m[pX, pY].Map[x - 1, y, z].sb) m[pX, pY].Map[x - 1, y, z] = checkvisiblesb(m, pX, pY, x - 1, y, z);
            }

            if (y - 1 < 0)
            {
                m[pX, pY - 1].Map[x, y + 63, z].bvis = checkvisible(m, pX, pY - 1, x, y + 63, z);
                if (m[pX, pY - 1].Map[x, y + 63, z].sb) m[pX, pY - 1].Map[x, y + 63, z] = checkvisiblesb(m, pX, pY - 1, x, y + 63, z);
            }
            else
            {
                m[pX, pY].Map[x, y - 1, z].bvis = checkvisible(m, pX, pY, x, y - 1, z);
                if (m[pX, pY].Map[x, y - 1, z].sb) m[pX, pY].Map[x, y - 1, z] = checkvisiblesb(m, pX, pY, x, y - 1, z);
            }

            if (x + 1 >= 64)
            {
                m[pX + 1, pY].Map[x - 63, y, z].bvis = checkvisible(m, pX + 1, pY, x - 63, y, z);
                if (m[pX + 1, pY].Map[x - 63, y, z].sb) m[pX + 1, pY].Map[x - 63, y, z] = checkvisiblesb(m, pX + 1, pY, x - 63, y, z);
            }
            else
            {
                m[pX, pY].Map[x + 1, y, z].bvis = checkvisible(m, pX, pY, x + 1, y, z);
                if (m[pX, pY].Map[x + 1, y, z].sb) m[pX, pY].Map[x + 1, y, z] = checkvisiblesb(m, pX, pY, x + 1, y, z);
            }

            if (y + 1 >= 64)
            {
                m[pX, pY + 1].Map[x, y - 63, z].bvis = checkvisible(m, pX, pY + 1, x, y - 63, z);
                if (m[pX, pY + 1].Map[x, y - 63, z].sb) m[pX, pY + 1].Map[x, y - 63, z] = checkvisiblesb(m, pX, pY + 1, x, y - 63, z);
            }
            else
            {
                m[pX, pY].Map[x, y + 1, z].bvis = checkvisible(m, pX, pY, x, y + 1, z);
                if (m[pX, pY].Map[x, y + 1, z].sb) m[pX, pY].Map[x, y + 1, z] = checkvisiblesb(m, pX, pY, x, y + 1, z);
            }
        }*/

        private MapList checkvisiblechunk(MapList[,] workM, int pX, int pY)
        {
            MapList outM = workM[pX, pY];
            for (byte x = 0; x < 64; x++)
            {
                for (byte y = 0; y < 64; y++)
                {
                    for (byte z = 0; z < 64; z++)
                    {
                        outM.Map[x, y, z].bvis = checkvisible(workM, pX, pY, x, y, z);
                        if (outM.Map[x, y, z].sb & outM.Map[x, y, z].bvis)
                        {
                            outM.Map[x, y, z] = checkvisiblesb(workM, pX, pY, x, y, z);
                        }
                        //outM.Map[x, y, z] = checktatches(workM, pX, pY, x, y, z);
                    }
                }
            }
            return outM;
        }

    /*    private strmesh checktatches(MapList[,] workM, int pX, int pY, int x, int y, int z)
        {
            strmesh blockcheck = workM[pX, pY].Map[x, y, z];
            for (byte sh = 0; sh < 6; sh++)
            {
                int workX = pX;
                int workY = pY;
                int workx = x + (int)shifting[sh].X;
                if (workx > 63) { if (workX < 3) { workx -= 64; workX++; } else workx = 63; } else if (workx < 0) { if (workX > 0) { workx += 64; workX--; } else workx = 0; }
                int worky = y + (int)shifting[sh].Y;
                if (worky > 63) { if (workY < 3) { worky -= 64; workY++; } else worky = 63; } else if (worky < 0) { if (workY > 0) { worky += 64; workY--; } else worky = 0; }
                int workz = z + (int)shifting[sh].Z;
                if (workz >= 64) { workz = 63; } else if (workz < 0) { workz = 0; }
                if (workM[workX, workY].Map[workx, worky, workz].taches[0, (sh / 2) * 2 + (sh +1) % 2] > 0)
                {
                    if (workM[pX, pY].Map[x, y, z].type > 0)
                    {
                        //if (workM[workX, workY].Map[workx, worky, workz].type == 0) { }
                        if (workM[workX, workY].Map[workx, worky, workz].taches[0, (sh / 2) * 2 + (sh + 1) % 2]>0)
                            workM[pX, pY].Map[x, y, z].taches[1, sh] = (byte)(workM[workX, workY].Map[workx, worky, workz].taches[0, (sh / 2) * 2 + (sh + 1) % 2] - 1);
                        if (workM[pX, pY].Map[x, y, z].sb) 
                            for (byte vb = 0; vb < 6; vb++) 
                                if (workM[workX, workY].Map[workx, worky, workz].taches[0, (sh / 2) * 2 + (sh + 1) % 2] > 9) 
                                    workM[pX, pY].Map[x, y, z].taches[0, vb] = (byte)(workM[workX, workY].Map[workx, worky, workz].taches[0, (sh / 2) * 2 + (sh + 1) % 2] - 10);
                    }
                    else
                    {
                        for (byte vb = 0; vb < 6; vb++)
                            workM[pX, pY].Map[x, y, z].taches[0, vb] = workM[workX, workY].Map[workx, worky, workz].taches[0, (sh / 2) * 2 + (sh + 1) % 2];
                    }
                }
            }
            return workM[pX, pY].Map[x, y, z];
        }*/

        private strmesh checkvisiblesb(MapList[,] workM, int pX, int pY, int x, int y, int z)
        {
            strmesh blockcheck = workM[pX, pY].Map[x, y, z];
            int sbx0;
            int sby0;
            int sbz0;
            int x0;
            int y0;
            int z0;
            int p0X;
            int p0Y;
            for (byte sbX = 0; sbX < 4; sbX++)
            {
                for (byte sbY = 0; sbY < 4; sbY++)
                {
                    for (byte sbZ = 0; sbZ < 4; sbZ++)
                    {
                        sbx0 = sbX - 1;
                        x0 = x;
                        p0X = pX;
                        if (sbx0 < 0) { x0--; sbx0 += 4; }
                        if (x0 < 0) { p0X--; x0 += 64; }
                        if (p0X < 0) blockcheck.sbvis[sbX, sbY, sbZ, 4] = false;
                        else
                        {
                            if (workM[p0X, pY].Map[x0, y, z].sb) blockcheck.sbvis[sbX, sbY, sbZ, 4] = (workM[p0X, pY].Map[x0, y, z].sbtype[sbx0, sbY, sbZ] != 0) ? false : true;
                            else blockcheck.sbvis[sbX, sbY, sbZ, 4] = (workM[p0X, pY].Map[x0, y, z].type != 0) ? false : true;
                        }
                        sbx0 = sbX + 1;
                        x0 = x;
                        p0X = pX;
                        if (sbx0 > 3) { x0++; sbx0 -= 4; }
                        if (x0 > 63) { p0X++; x0 -= 64; }
                        if (p0X > 3) blockcheck.sbvis[sbX, sbY, sbZ, 5] = false;
                        else
                        {
                            if (workM[p0X, pY].Map[x0, y, z].sb) blockcheck.sbvis[sbX, sbY, sbZ, 5] = (workM[p0X, pY].Map[x0, y, z].sbtype[sbx0, sbY, sbZ] != 0) ? false : true;
                            else blockcheck.sbvis[sbX, sbY, sbZ, 5] = (workM[p0X, pY].Map[x0, y, z].type != 0) ? false : true;
                        }

                        sby0 = sbY - 1;
                        y0 = y;
                        p0Y = pY;
                        if (sby0 < 0) { y0--; sby0 += 4; }
                        if (y0 < 0) { p0Y--; y0 += 64; }
                        if (p0Y < 0) blockcheck.sbvis[sbX, sbY, sbZ, 2] = false;
                        else
                        {
                            if (workM[pX, p0Y].Map[x, y0, z].sb) blockcheck.sbvis[sbX, sbY, sbZ, 2] = (workM[pX, p0Y].Map[x, y0, z].sbtype[sbX, sby0, sbZ] != 0) ? false : true;
                            else blockcheck.sbvis[sbX, sbY, sbZ, 2] = (workM[pX, p0Y].Map[x, y0, z].type != 0) ? false : true;
                        }
                        sby0 = sbY + 1;
                        y0 = y;
                        p0Y = pY;
                        if (sby0 > 3) { y0++; sby0 -= 4; }
                        if (y0 > 63) { p0Y++; y0 -= 64; }
                        if (p0Y > 3) blockcheck.sbvis[sbX, sbY, sbZ, 3] = false;
                        else
                        {
                            if (workM[pX, p0Y].Map[x, y0, z].sb) blockcheck.sbvis[sbX, sbY, sbZ, 3] = (workM[pX, p0Y].Map[x, y0, z].sbtype[sbX, sby0, sbZ] != 0) ? false : true;
                            else blockcheck.sbvis[sbX, sbY, sbZ, 3] = (workM[pX, p0Y].Map[x, y0, z].type != 0) ? false : true;
                        }

                        sbz0 = sbZ - 1;
                        z0 = z;

                        if (sbz0 < 0) { z0--; sbz0 += 4; }
                        if (z0 < 0) blockcheck.sbvis[sbX, sbY, sbZ, 1] = false;
                        else
                        {
                            if (workM[pX, pY].Map[x, y, z0].sb) blockcheck.sbvis[sbX, sbY, sbZ, 1] = (workM[pX, pY].Map[x, y, z0].sbtype[sbX, sbY, sbz0] != 0) ? false : true;
                            else blockcheck.sbvis[sbX, sbY, sbZ, 1] = (workM[pX, pY].Map[x, y, z0].type != 0) ? false : true;
                        }
                        sbz0 = sbZ + 1;
                        z0 = z;

                        if (sbz0 > 3) { z0++; sbz0 -= 4; }
                        if (z0 >= 64) blockcheck.sbvis[sbX, sbY, sbZ, 0] = false;
                        else
                        {
                            if (workM[pX, pY].Map[x, y, z0].sb) blockcheck.sbvis[sbX, sbY, sbZ, 0] = (workM[pX, pY].Map[x, y, z0].sbtype[sbX, sbY, sbz0] != 0) ? false : true;
                            else blockcheck.sbvis[sbX, sbY, sbZ, 0] = (workM[pX, pY].Map[x, y, z0].type != 0) ? false : true;
                        }
                    }
                }
            }
            return blockcheck;
        }

        private bool checkvisible(MapList[,] workM, int pX, int pY, int x, int y, int z)
        {
            int x0;
            int y0;
            int z0;
            int p0X;
            int p0Y;
            if (workM[pX, pY].Map[x, y, z].type == 0)
            {
                return false;
            }
            z0 = (z - 1 < 0) ? z : z - 1;
            if (workM[pX, pY].Map[x, y, z0].type != 0 & !workM[pX, pY].Map[x, y, z0].sb)
            {
                z0 = (z + 1 >= 64) ? z : z + 1;
                if (workM[pX, pY].Map[x, y, z0].type != 0 & !workM[pX, pY].Map[x, y, z0].sb)
                {
                    if (x - 1 < 0) { p0X = pX - 1; x0 = x + 63; }
                    else { p0X = pX; x0 = x - 1; }
                    if (p0X >= 0)
                    {
                        if (workM[p0X, pY].Map[x0, y, z].type != 0 & !workM[p0X, pY].Map[x0, y, z].sb)
                        {
                            if (x + 1 >= 64) { p0X = pX + 1; x0 = x - 63; }
                            else { p0X = pX; x0 = x + 1; }
                            if (p0X < 4)
                            {
                                if (workM[p0X, pY].Map[x0, y, z].type != 0 & !workM[p0X, pY].Map[x0, y, z].sb)
                                {
                                    if (y - 1 < 0) { p0Y = pY - 1; y0 = y + 63; }
                                    else { p0Y = pY; y0 = y - 1; }
                                    if (p0Y >= 0)
                                    {
                                        if (workM[pX, p0Y].Map[x, y0, z].type != 0 & !workM[pX, p0Y].Map[x, y0, z].sb)
                                        {
                                            if (y + 1 >= 64) { p0Y = pY + 1; y0 = y - 63; }
                                            else { p0Y = pY; y0 = y + 1; }
                                            if (p0Y < 4)
                                            {
                                                if (workM[pX, p0Y].Map[x, y0, z].type != 0 & !workM[pX, p0Y].Map[x, y0, z].sb)
                                                {
                                                    return false;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return true;
        }

        private void Form1_MouseWheel(object sender, MouseEventArgs e)
        {
            if (LeftHand)
            {
                if (e.Delta < 0)
                {
                    handLUse--;
                    if (handLUse < 10) handLUse = (sbyte)(9 + leftsize);
                }
                if (e.Delta > 0)
                {
                    handLUse++;
                    if (handLUse >= 10 + leftsize) handLUse = 10;
                }
            }
            else
            {
                if (e.Delta < 0)
                {
                    handRUse++;
                    if (handRUse >= rightsize) handRUse = 0;
                }
                if (e.Delta > 0)
                {
                    handRUse--;
                    if (handRUse < 0) handRUse = (sbyte)(rightsize - 1);
                }
            }
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) //открытие и закрытие меню
            {
                if (going == false)
                {
                    panel1.Visible = false;
                    panel1.Enabled = false;
                    trackBar1.Enabled = false;
                    menuStrip1.Visible = false;
                    HighRes_Butt.Enabled = false;
                    HighRes_Butt.Visible = false;
                    Cursor.Hide();
                    Cursor.Position = center();
                    going = true;
                }
                else
                {
                    panel1.Visible = true;
                    panel1.Enabled = true;
                    trackBar1.Enabled = true;
                    menuStrip1.Visible = true;
                    HighRes_Butt.Enabled = true;
                    HighRes_Butt.Visible = true;
                    Cursor.Show();
                    going = false;
                    if (InventoryOpen) { InventoryOpen = false; Cursor.Hide(); }
                }
            }
            if (e.KeyCode == Keys.F3) techno = (techno) ? false : true;
            if (going)
            {
                if (e.KeyCode == Keys.W) stepfb = 0.1f;
                if (e.KeyCode == Keys.S) stepfb = -0.1f;
                if (e.KeyCode == Keys.A) steprl = 0.1f;
                if (e.KeyCode == Keys.D) steprl = -0.1f;
                if (e.KeyCode == Keys.ShiftKey) Run = runing;
                if (e.KeyCode == Keys.Space & !flight)
                {
                    accelerate = 1.4f;
                    flight = true;
                }
              //  if (e.KeyCode == Keys.L) light = (light == true) ? false : true;
                if (e.KeyCode == Keys.ControlKey) LeftHand = true;
                if (e.KeyCode == Keys.I) //открытие и закрытие инвентаря
                {
                    if (InventoryOpen == true)
                    {
                        Cursor.Hide();
                        Cursor.Position = center();
                        InventoryOpen = false;
                    }
                    else
                    {
                        Cursor.Show();
                        InventoryOpen = true;
                    }
                }
            }
        }

        private void Form1_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.W) stepfb = 0;
            if (e.KeyCode == Keys.S) stepfb = 0;
            if (e.KeyCode == Keys.A) steprl = 0;
            if (e.KeyCode == Keys.D) steprl = 0;
            if (e.KeyCode == Keys.ShiftKey) Run = walk;
            if (e.KeyCode == Keys.ControlKey) LeftHand = false;
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            if (framenum < 9 | framenum > 11)
            {
                if (framenum < 9 & cps < 80)
                { cps += 2; }
                if (framenum > 11 & cps > 3)
                { cps -= 3; }
                gametimer.Interval = 1000 / cps;
            }
            sfps += framenum;
            framenum = 0;
            fpstick++;
            if (fpstick == 4)
            {
                fps = sfps;
                sfps = 0;
                fpstick = 0;
            }
            test = 0;
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            try { t0.Abort(); }
            catch { }
        }

        private void trackBar1_Scroll(object sender, EventArgs e)
        {
            visible = (byte)trackBar1.Value;
            
            this.Focus();
        }

        private void продолжитьToolStripMenuItem_Click(object sender, EventArgs e)
        {
            panel1.Visible = false;
            panel1.Enabled = false;
            trackBar1.Enabled = false;
            menuStrip1.Visible = false;
            HighRes_Butt.Enabled = false;
            HighRes_Butt.Visible = false;
            this.Focus();
            Cursor.Hide();
            Cursor.Position = center();
            going = true;
        }

        

        

    
    }
}
