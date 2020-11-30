using Microsoft.Azure.CognitiveServices.Vision.Face;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace poc_biometria_facial_azure
{
    class Program
    {
        const string SUBSCRIPTION_KEY = "f3bdc0816f504b8c822e589209c00094";
        const string ENDPOINT = "https://pocbiometriafacial.cognitiveservices.azure.com/";

        const string RECOGNITION_MODEL = RecognitionModel.Recognition03;

        const string IMAGE_BASE_URL = "https://testebiometria.blob.core.windows.net/teste/";
        const string IMAGEM1 = "NEYMAR.jpeg";
        const string IMAGEM2 = "NEYMAR2.jpg";
        const string IMAGEM3 = "NEYMAR3.jpg";
        const string IMAGEM4 = "MBAPPE2.jpg";
        const string IMAGEM5 = "CRISTIANO.jpeg";

        static void Main(string[] args)
        {
            IFaceClient faceClient = Authenticate(ENDPOINT, SUBSCRIPTION_KEY); // AUTENTICAR SERVIÇO

            Verify(faceClient, $"{IMAGE_BASE_URL}{IMAGEM1}", $"{IMAGE_BASE_URL}{IMAGEM2}", RECOGNITION_MODEL).Wait(); //VERIFICA DUAS IMAGENS, SEM ARMAZENAR FACES NA AZURE

            var largePersonGroup = CreateLargePersonGroup(faceClient, RECOGNITION_MODEL); // CRIA UM GRANDE GRUPO DE PESSOAS
            largePersonGroup.Wait();

            var person = CreatePerson(faceClient, largePersonGroup.Result); // CRIA UMA PESSOA E A VINCULA A UM GRUPO DE PESSOAS
            person.Wait();

            var face = AddFaceToPerson(faceClient, $"{IMAGE_BASE_URL}{IMAGEM3}", largePersonGroup.Result, person.Result); // ADICIONA UMA FACE A UMA PESSOA
            face.Wait();
            
            TrainLargePersonGroup(faceClient, largePersonGroup.Result).Wait(); // TREINA O GRUPO, NECESSÁRIO PARA MÉTODO 'IdentifyInLargePersonGroup' FUNCIONAR

            Verify(faceClient, $"{IMAGE_BASE_URL}{IMAGEM1}", person.Result, largePersonGroup.Result, RECOGNITION_MODEL).Wait(); // VERIFICA SE A IMAGEM CORRESPONDE A ALGUMA FACE DA PESSOA INFORMADA
            
            IdentifyInLargePersonGroup(faceClient, $"{IMAGE_BASE_URL}{IMAGEM1}", largePersonGroup.Result, RECOGNITION_MODEL).Wait(); // VERIFICA SE A IMAGEM CORRESPONDE A ALGUMA FACE DE ALGUMA PESSOA DO GRUPO

            DeleteLargePersonGroup(faceClient, largePersonGroup.Result).Wait(); // DELETAR UM LARGE PERSON GROUP
        }

        public static IFaceClient Authenticate(string endpoint, string key)
        {
            return new FaceClient(new ApiKeyServiceClientCredentials(key)) { Endpoint = endpoint };
        }

        private static async Task<List<DetectedFace>> DetectFaceRecognize(IFaceClient faceClient, string urlImagem, string recognitionModel)
        {
            IList<DetectedFace> detectedFaces = await faceClient.Face.DetectWithUrlAsync(urlImagem, recognitionModel: recognitionModel);
            return detectedFaces.ToList();
        }

        public static async Task Verify(IFaceClient client, string urlImagem1, string urlImagem2, string recognitionModel)
        {
            List<DetectedFace> detectedFaces1 = await DetectFaceRecognize(client, urlImagem1, recognitionModel);
            Guid sourceFaceId1 = detectedFaces1[0].FaceId.Value;

            List<DetectedFace> detectedFaces2 = await DetectFaceRecognize(client, urlImagem2, recognitionModel);
            Guid sourceFaceId2 = detectedFaces2[0].FaceId.Value;

            VerifyResult verifyResult1 = await client.Face.VerifyFaceToFaceAsync(sourceFaceId1, sourceFaceId2);

            Console.WriteLine(
                verifyResult1.IsIdentical
                    ? $"Faces are of the same (Positive) person, similarity confidence: {verifyResult1.Confidence}."
                    : $"Faces are of different (Negative) persons, similarity confidence: {verifyResult1.Confidence}.");
        }

        public static async Task Verify(IFaceClient client, string urlImagem1, Guid personId, string largePersonGroupId, string recognitionModel)
        {
            List<DetectedFace> detectedFaces1 = await DetectFaceRecognize(client, urlImagem1, recognitionModel);
            Guid sourceFaceId1 = detectedFaces1[0].FaceId.Value;

            VerifyResult verifyResult1 = await client.Face.VerifyFaceToPersonAsync(sourceFaceId1, personId, largePersonGroupId: largePersonGroupId);

            Console.WriteLine(
                verifyResult1.IsIdentical
                    ? $"Faces are of the same (Positive) person, similarity confidence: {verifyResult1.Confidence}."
                    : $"Faces are of different (Negative) persons, similarity confidence: {verifyResult1.Confidence}.");
        }

        public static async Task IdentifyInLargePersonGroup(IFaceClient client, string urlImagem, string largePersonGroupId, string recognitionModel)
        {
            List<DetectedFace> detectedFaces1 = await DetectFaceRecognize(client, urlImagem, recognitionModel);
            Guid sourceFaceId1 = detectedFaces1[0].FaceId.Value;
            List<Guid?> faceIds = new List<Guid?> { sourceFaceId1 };

            var identifyResults = await client.Face.IdentifyAsync(faceIds, largePersonGroupId:largePersonGroupId);

            foreach (var identifyResult in identifyResults)
            {
                Console.WriteLine($"PersonID: {identifyResult.Candidates[0].PersonId}" +
                    $" Confidence: {identifyResult.Candidates[0].Confidence}.");
            }
        }

        public static async Task TrainLargePersonGroup(IFaceClient client, string largePersonGroupId)
        {
            await client.LargePersonGroup.TrainAsync(largePersonGroupId);

            // Wait until the training is completed.
            while (true)
            {
                await Task.Delay(1000);
                var trainingStatus = await client.LargePersonGroup.GetTrainingStatusAsync(largePersonGroupId);
                if (trainingStatus.Status == TrainingStatusType.Succeeded) { break; }
            }
        }

        public static async Task<string> CreateLargePersonGroup(IFaceClient client, string recognitionModel)
        {
            string largePersonGroupId = Guid.NewGuid().ToString();
            await client.LargePersonGroup.CreateAsync(largePersonGroupId: largePersonGroupId, name: "GRUPO TESTE", recognitionModel: recognitionModel);
            return largePersonGroupId;
        }

        public static async Task<Guid> CreatePerson(IFaceClient client, string largePersonGroupId)
        {
            Person person = await client.LargePersonGroupPerson.CreateAsync(largePersonGroupId: largePersonGroupId, name: "TESTE PERSON");
            return person.PersonId;
        }

        public static async Task<Guid> AddFaceToPerson(IFaceClient client, string urlImagem, string largePersonGroupId, Guid personId)
        {
            PersistedFace persistedFace = await client.LargePersonGroupPerson.AddFaceFromUrlAsync(largePersonGroupId, personId, urlImagem);
            return persistedFace.PersistedFaceId;
        }

        public static async Task DeleteLargePersonGroup(IFaceClient client, String largePersonGroupId)
        {
            await client.LargePersonGroup.DeleteAsync(largePersonGroupId);
        }
    }
}
